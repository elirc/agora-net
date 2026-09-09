using System.Net;
using Agora.Domain.Services;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Agora.Tests.Integration;

public class WebhookOutboxTests
{
    [Fact]
    public async Task Commit_with_worker_stopped_is_durable_and_run_once_sends_without_replaying_business_work()
    {
        var sender = new RecordingSender(true, 200); using var scenario = await ReportTestScenario.Create(sender.Register);
        Guid deliveryId = default; Guid eventId = default;
        await scenario.Db(async db =>
        {
            var subscription = new WebhookSubscription { Url = "https://example.test/durable", Secret = "sixteen-character-secret", Events = [WebhookEvents.OrderPaid] };
            db.WebhookSubscriptions.Add(subscription); await db.SaveChangesAsync();
            var service = new WebhookService(db, scenario.Clock);
            var staged = await service.StageAsync(WebhookEvents.OrderPaid, new { orderNumber = "ORD-DURABLE", total = 12.34m }, scenario.Clock.Instant);
            await db.SaveChangesAsync(); eventId = staged.Id; deliveryId = Assert.Single(staged.Deliveries).Id;
        });
        Assert.Equal(0, sender.Calls);
        await scenario.Db(async db => Assert.Equal(1, await new WebhookOutboxRunner(db, scenario.Clock,
            scenario.App.Services.GetRequiredService<IServiceScopeFactory>()).RunOnceAsync()));
        Assert.Equal(1, sender.Calls);
        await scenario.Db(async db =>
        {
            Assert.NotNull(await db.Set<OutboxEvent>().FindAsync(eventId));
            var delivery = await db.WebhookDeliveries.SingleAsync(d => d.Id == deliveryId);
            Assert.Equal(WebhookDeliveryStatus.Succeeded, delivery.Status); Assert.Equal(1, delivery.AttemptCount);
            var attempt = await db.Set<WebhookAttempt>().SingleAsync(a => a.DeliveryId == deliveryId);
            Assert.Equal(WebhookAttemptOutcome.Succeeded, attempt.Outcome); Assert.NotNull(attempt.SendInitiatedAt);
        });
    }

    [Fact]
    public async Task Events_exist_without_subscribers_and_failed_attempts_use_reserved_slots_and_due_schedule()
    {
        var sender = new RecordingSender(false, 503); using var scenario = await ReportTestScenario.Create(sender.Register); Guid deliveryId = default;
        await scenario.Db(async db => { await new WebhookService(db, scenario.Clock).StageAsync(WebhookEvents.OrderCreated, new { number = "NO-SUB" }, scenario.Clock.Instant); await db.SaveChangesAsync(); });
        await scenario.Db(async db => Assert.Single(await db.Set<OutboxEvent>().ToListAsync()));
        await scenario.Db(async db =>
        {
            var sub = new WebhookSubscription { Url = "https://example.test/fail", Secret = "sixteen-character-secret", Events = [WebhookEvents.OrderPaid] };
            db.Add(sub); await db.SaveChangesAsync(); var evt = await new WebhookService(db, scenario.Clock).StageAsync(WebhookEvents.OrderPaid, new { number = "FAIL" }, scenario.Clock.Instant);
            await db.SaveChangesAsync(); deliveryId = evt.Deliveries.Single().Id;
        });
        await scenario.Db(async db => await new WebhookOutboxRunner(db, scenario.Clock,
            scenario.App.Services.GetRequiredService<IServiceScopeFactory>()).RunOnceAsync());
        await scenario.Db(async db =>
        {
            var delivery = await db.WebhookDeliveries.SingleAsync(d => d.Id == deliveryId);
            Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status); Assert.Equal(1, delivery.AttemptCount);
            Assert.Equal(scenario.Clock.Instant.AddMinutes(1), delivery.DueAt);
            Assert.Equal(WebhookAttemptOutcome.Failed, (await db.Set<WebhookAttempt>().SingleAsync(a => a.DeliveryId == deliveryId)).Outcome);
        });
    }

    [Fact]
    public async Task Replay_uses_original_event_identity_and_current_destination_without_sending_inline()
    {
        using var scenario = await ReportTestScenario.Create(); Guid eventId = default; Guid subscriptionId = default;
        await scenario.Db(async db =>
        {
            var evt = new OutboxEvent { EventType = WebhookEvents.OrderPaid, SchemaVersion = 1,
                DataJson = "{\"orderNumber\":\"ORD-OLD\",\"total\":10}", OccurredAt = scenario.Clock.Instant.AddDays(-1) };
            var sub = new WebhookSubscription { Url = "https://example.test/new-consumer", Secret = "sixteen-character-secret", Events = [WebhookEvents.OrderPaid] };
            db.AddRange(evt, sub); await db.SaveChangesAsync(); eventId = evt.Id; subscriptionId = sub.Id;
        });
        var operation = Guid.NewGuid();
        var response = await scenario.Admin.PostAsJsonAsync("/api/admin/webhook-replays", new CreateWebhookReplayRequest(operation, subscriptionId, [eventId]));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); var receipt = (await response.Content.ReadFromJsonAsync<WebhookReplayResponse>())!;
        Assert.Equal("Enqueued", Assert.Single(receipt.Results).Status);
        await scenario.Db(async db =>
        {
            var delivery = await db.WebhookDeliveries.SingleAsync(d => d.EventId == eventId && d.SubscriptionId == subscriptionId);
            Assert.Contains("ORD-OLD", delivery.Payload); Assert.Contains(eventId.ToString(), delivery.Payload); Assert.Equal(0, delivery.AttemptCount);
        });
        var replay = await scenario.Admin.PostAsJsonAsync("/api/admin/webhook-replays", new CreateWebhookReplayRequest(operation, subscriptionId, [eventId]));
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode); var same = (await replay.Content.ReadFromJsonAsync<WebhookReplayResponse>())!;
        Assert.Equal(receipt.OperationId, same.OperationId); Assert.Equal(receipt.Results.Single(), same.Results.Single());
        var changed = await scenario.Admin.PostAsJsonAsync("/api/admin/webhook-replays",
            new CreateWebhookReplayRequest(operation, subscriptionId, [Guid.NewGuid()]));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
    }

    [Fact]
    public async Task Replay_rejects_missing_old_unsupported_and_unsubscribed_sets_atomically()
    {
        using var scenario = await ReportTestScenario.Create(); Guid subscriptionId = default; Guid oldId = default; Guid unsupportedId = default; Guid wrongTypeId = default;
        await scenario.Db(async db =>
        {
            var sub = new WebhookSubscription { Url = "https://example.test/target", Secret = "sixteen-character-secret", Events = [WebhookEvents.OrderPaid] };
            var old = new OutboxEvent { EventType = WebhookEvents.OrderPaid, SchemaVersion = 1, DataJson = "{}", OccurredAt = scenario.Clock.Instant.AddDays(-30).AddTicks(-1) };
            var unsupported = new OutboxEvent { EventType = WebhookEvents.OrderPaid, SchemaVersion = 2, DataJson = "{}", OccurredAt = scenario.Clock.Instant };
            var wrong = new OutboxEvent { EventType = WebhookEvents.OrderRefunded, SchemaVersion = 1, DataJson = "{}", OccurredAt = scenario.Clock.Instant };
            db.AddRange(sub, old, unsupported, wrong); await db.SaveChangesAsync(); subscriptionId = sub.Id; oldId = old.Id; unsupportedId = unsupported.Id; wrongTypeId = wrong.Id;
        });
        foreach (var ids in new[] { new[] { Guid.NewGuid() }, new[] { oldId }, new[] { unsupportedId }, new[] { wrongTypeId }, new[] { oldId, unsupportedId } })
        {
            var response = await scenario.Admin.PostAsJsonAsync("/api/admin/webhook-replays", new CreateWebhookReplayRequest(Guid.NewGuid(), subscriptionId, ids.ToList()));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }
        await scenario.Db(async db =>
        {
            Assert.Empty(await db.Set<WebhookReplayBatch>().ToListAsync()); Assert.Empty(await db.WebhookDeliveries.ToListAsync());
        });
    }

    private sealed class RecordingSender(bool succeeds, int code) : IWebhookSender
    {
        public int Calls;
        public void Register(IServiceCollection services) { services.RemoveAll<IWebhookSender>(); services.AddSingleton<IWebhookSender>(this); }
        public Task<WebhookSendResult> SendAsync(string url, string payload, string signature, CancellationToken ct = default)
        { Calls++; return Task.FromResult(new WebhookSendResult(succeeds, code)); }
    }
}
