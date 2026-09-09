using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class WebhookOutboxPersistenceTests
{
    [Fact]
    public async Task Upgrade_preserves_legacy_delivery_evidence_without_fabricating_events_or_attempts()
    {
        await using var store = new Store(); var subId = Guid.NewGuid(); var pendingId = Guid.NewGuid(); var failedId = Guid.NewGuid();
        var successId = Guid.NewGuid(); var exhaustedId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        using (var oldScope = store.Provider.CreateScope())
        {
            var old = oldScope.ServiceProvider.GetRequiredService<AgoraDbContext>(); await old.Database.MigrateAsync();
            await old.GetService<IMigrator>().MigrateAsync("20260908224638_SellingWarehouseAndAccessPolicies");
            await old.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO WebhookSubscriptions(Id,Url,Secret,Events,IsActive,CreatedAt)
VALUES({subId},{"https://example.test/legacy"},{"legacy-secret-1234"},{"order.paid"},{true},{now.UtcTicks})");
            async Task Insert(Guid id, int status, int attempts, int? code) => await old.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO WebhookDeliveries
(Id,SubscriptionId,EventType,Payload,Signature,Status,AttemptCount,LastResponseStatusCode,LastAttemptAt,CreatedAt)
VALUES({id},{subId},{"order.paid"},{"LEGACY-PAYLOAD"},{"LEGACY-SIGNATURE"},{status},{attempts},{code},{now.UtcTicks},{now.UtcTicks})");
            await Insert(pendingId, 0, 0, null); await Insert(failedId, 2, 3, 503); await Insert(successId, 1, 2, 200); await Insert(exhaustedId, 2, 5, 503);
        }
        using var upgradedScope = store.Provider.CreateScope(); var upgraded = upgradedScope.ServiceProvider.GetRequiredService<AgoraDbContext>(); await upgraded.Database.MigrateAsync();
        Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync()); Assert.Empty(await upgraded.Set<OutboxEvent>().ToListAsync()); Assert.Empty(await upgraded.Set<WebhookAttempt>().ToListAsync());
        var rows = await upgraded.WebhookDeliveries.OrderBy(d => d.AttemptCount).ToListAsync();
        Assert.All(rows, d => { Assert.Null(d.EventId); Assert.Equal("https://example.test/legacy", d.DestinationUrl); Assert.Equal(d.AttemptCount + 1, d.HistoryStartsAtAttempt); Assert.Equal("LEGACY-PAYLOAD", d.Payload); Assert.Equal("LEGACY-SIGNATURE", d.Signature); });
        Assert.NotNull(rows.Single(d => d.Id == pendingId).DueAt); Assert.NotNull(rows.Single(d => d.Id == failedId).DueAt);
        Assert.Null(rows.Single(d => d.Id == successId).DueAt); Assert.Null(rows.Single(d => d.Id == exhaustedId).DueAt);
    }

    [Fact]
    public async Task Independent_workers_compete_for_one_claim_and_send_once()
    {
        await using var store = new Store(); var deliveryId = await store.Seed();
        async Task<string> Run()
        {
            using var scope = store.Provider.CreateScope();
            try { return (await scope.ServiceProvider.GetRequiredService<WebhookOutboxRunner>().RunOnceAsync()) == 1 ? "claimed" : "empty"; }
            catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6) { return "busy"; }
            catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 5 or 6 }) { return "busy"; }
        }
        var results = await Task.WhenAll(Task.Run(Run), Task.Run(Run));
        Assert.Single(results, value => value == "claimed");
        Assert.Equal(1, store.Sender.Calls);
        using var checkScope = store.Provider.CreateScope(); var db = checkScope.ServiceProvider.GetRequiredService<AgoraDbContext>();
        var delivery = await db.WebhookDeliveries.SingleAsync(d => d.Id == deliveryId);
        Assert.Equal(WebhookDeliveryStatus.Succeeded, delivery.Status); Assert.Equal(1, delivery.AttemptCount);
        Assert.Single(await db.Set<WebhookAttempt>().Where(a => a.DeliveryId == deliveryId).ToListAsync());
    }

    [Fact]
    public async Task Failed_attempt_insert_rolls_back_reserved_slot()
    {
        await using var store = new Store(); var deliveryId = await store.Seed();
        using (var triggerScope = store.Provider.CreateScope())
        {
            var db = triggerScope.ServiceProvider.GetRequiredService<AgoraDbContext>();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_webhook_attempt BEFORE INSERT ON WebhookAttempts BEGIN SELECT RAISE(ABORT, 'forced attempt failure'); END;");
        }
        using (var runScope = store.Provider.CreateScope())
            await Assert.ThrowsAnyAsync<Exception>(() => runScope.ServiceProvider.GetRequiredService<WebhookOutboxRunner>().RunOnceAsync());
        using var checkScope = store.Provider.CreateScope(); var check = checkScope.ServiceProvider.GetRequiredService<AgoraDbContext>();
        var delivery = await check.WebhookDeliveries.SingleAsync(d => d.Id == deliveryId);
        Assert.Equal(0, delivery.AttemptCount); Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status);
        Assert.Empty(await check.Set<WebhookAttempt>().ToListAsync()); Assert.Equal(0, store.Sender.Calls);
    }

    [Fact]
    public async Task Replay_receipt_failure_rolls_back_new_delivery_and_batch()
    {
        await using var store = new Store();
        Guid eventId; Guid subscriptionId;
        using (var setupScope = store.Provider.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<AgoraDbContext>(); await db.Database.EnsureCreatedAsync();
            var evt = new OutboxEvent { EventType = WebhookEvents.OrderPaid, SchemaVersion = 1, DataJson = "{\"number\":\"ROLLBACK\"}", OccurredAt = DateTimeOffset.UtcNow };
            var sub = new WebhookSubscription { Url = "https://example.test/replay", Secret = "sixteen-character-secret", Events = [WebhookEvents.OrderPaid] };
            db.AddRange(evt, sub); await db.SaveChangesAsync(); eventId = evt.Id; subscriptionId = sub.Id;
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_replay_result BEFORE INSERT ON WebhookReplayResults BEGIN SELECT RAISE(ABORT, 'forced replay receipt failure'); END;");
        }
        using (var replayScope = store.Provider.CreateScope())
        {
            var service = new WebhookReplayService(replayScope.ServiceProvider.GetRequiredService<AgoraDbContext>(), TimeProvider.System);
            await Assert.ThrowsAnyAsync<Exception>(() => service.ReplayAsync(Guid.NewGuid(), subscriptionId, [eventId], Guid.NewGuid()));
        }
        using var checkScope = store.Provider.CreateScope(); var check = checkScope.ServiceProvider.GetRequiredService<AgoraDbContext>();
        Assert.Empty(await check.Set<WebhookReplayBatch>().ToListAsync());
        Assert.Empty(await check.WebhookDeliveries.Where(d => d.EventId == eventId && d.SubscriptionId == subscriptionId).ToListAsync());
    }

    [Fact]
    public async Task Expired_first_worker_becomes_unknown_and_its_late_success_cannot_overwrite_attempt_two()
    {
        await using var store = new LateAckStore(); var deliveryId = await store.Seed();
        var firstRun = Task.Run(async () =>
        {
            using var scope = store.Provider.CreateScope(); return await scope.ServiceProvider.GetRequiredService<WebhookOutboxRunner>().RunOnceAsync();
        });
        await store.Sender.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        store.Clock.Instant = store.Clock.Instant.AddSeconds(61);
        using (var recovery = store.Provider.CreateScope())
            Assert.Equal(0, await recovery.ServiceProvider.GetRequiredService<WebhookOutboxRunner>().RunOnceAsync());
        using (var second = store.Provider.CreateScope())
            Assert.Equal(1, await second.ServiceProvider.GetRequiredService<WebhookOutboxRunner>().RunOnceAsync());
        store.Sender.ReleaseFirst.TrySetResult(new WebhookSendResult(true, 200));
        Assert.Equal(1, await firstRun.WaitAsync(TimeSpan.FromSeconds(10)));
        using var checkScope = store.Provider.CreateScope(); var db = checkScope.ServiceProvider.GetRequiredService<AgoraDbContext>();
        var delivery = await db.WebhookDeliveries.SingleAsync(d => d.Id == deliveryId);
        var attempts = await db.Set<WebhookAttempt>().Where(a => a.DeliveryId == deliveryId).OrderBy(a => a.AttemptNumber).ToListAsync();
        Assert.Equal(WebhookDeliveryStatus.Succeeded, delivery.Status); Assert.Equal(2, delivery.AttemptCount); Assert.Equal(2, delivery.LeaseGeneration);
        Assert.Equal(WebhookAttemptOutcome.Unknown, attempts[0].Outcome); Assert.Equal("LeaseExpired", attempts[0].ReasonCode);
        Assert.Equal(WebhookAttemptOutcome.Succeeded, attempts[1].Outcome); Assert.Equal(2, store.Sender.Calls);
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-webhook-outbox-" + Guid.NewGuid().ToString("N") + ".db");
        public ServiceProvider Provider { get; }
        public CountingSender Sender { get; } = new();
        public Store()
        {
            var services = new ServiceCollection();
            services.AddLogging(); services.AddSingleton<TimeProvider>(TimeProvider.System); services.AddSingleton<IWebhookSender>(Sender);
            services.AddDbContext<AgoraDbContext>(o => o.UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=30"));
            services.AddScoped<WebhookOutboxRunner>(); services.AddScoped<WebhookDeliverySender>(); Provider = services.BuildServiceProvider();
        }
        public async Task<Guid> Seed()
        {
            using var scope = Provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>(); await db.Database.EnsureCreatedAsync();
            var sub = new WebhookSubscription { Url = "https://example.test/outbox", Secret = "sixteen-character-secret", Events = [WebhookEvents.OrderPaid] };
            db.Add(sub); await db.SaveChangesAsync(); var evt = await new WebhookService(db, TimeProvider.System).StageAsync(WebhookEvents.OrderPaid, new { number = "RACE" }, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(); return evt.Deliveries.Single().Id;
        }
        public async ValueTask DisposeAsync() { await Provider.DisposeAsync(); File.Delete(_path); }
    }
    private sealed class CountingSender : IWebhookSender
    {
        private int _calls; public int Calls => Volatile.Read(ref _calls);
        public Task<WebhookSendResult> SendAsync(string url, string payload, string signature, CancellationToken ct = default)
        { Interlocked.Increment(ref _calls); return Task.FromResult(new WebhookSendResult(true, 200)); }
    }
    private sealed class MutableClock : TimeProvider { public DateTimeOffset Instant { get; set; } = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Instant; }
    private sealed class SequencedSender : IWebhookSender
    {
        private int _calls; public int Calls => Volatile.Read(ref _calls);
        public TaskCompletionSource<bool> FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<WebhookSendResult> ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WebhookSendResult> SendAsync(string url, string payload, string signature, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _calls) == 1) { FirstStarted.TrySetResult(true); return ReleaseFirst.Task; }
            return Task.FromResult(new WebhookSendResult(true, 200));
        }
    }
    private sealed class LateAckStore : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-webhook-late-" + Guid.NewGuid().ToString("N") + ".db");
        public MutableClock Clock { get; } = new(); public SequencedSender Sender { get; } = new(); public ServiceProvider Provider { get; }
        public LateAckStore()
        {
            var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(Clock); services.AddSingleton<IWebhookSender>(Sender);
            services.AddDbContext<AgoraDbContext>(o => o.UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=30"));
            services.AddScoped<WebhookOutboxRunner>(); services.AddScoped<WebhookDeliverySender>(); Provider = services.BuildServiceProvider();
        }
        public async Task<Guid> Seed()
        {
            using var scope = Provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>(); await db.Database.EnsureCreatedAsync();
            var sub = new WebhookSubscription { Url = "https://example.test/late", Secret = "sixteen-character-secret", Events = [WebhookEvents.OrderPaid] };
            db.Add(sub); await db.SaveChangesAsync(); var evt = await new WebhookService(db, Clock).StageAsync(WebhookEvents.OrderPaid, new { number = "LATE" }, Clock.Instant);
            await db.SaveChangesAsync(); return evt.Deliveries.Single().Id;
        }
        public async ValueTask DisposeAsync() { await Provider.DisposeAsync(); File.Delete(_path); }
    }
}
