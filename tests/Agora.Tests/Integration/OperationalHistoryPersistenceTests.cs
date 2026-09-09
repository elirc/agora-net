using System.Data.Common;
using System.Security.Claims;
using Agora.Api.Contracts;
using Agora.Api.Controllers;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;

namespace Agora.Tests.Integration;

public class OperationalHistoryPersistenceTests
{
    [Fact]
    public async Task Two_evidence_additions_at_four_cannot_exceed_five_or_touch_return_state()
    {
        await using var store = new Store(); var data = await store.Seed();
        await using (var db = store.Context())
        {
            db.ReturnEvidence.AddRange(Enumerable.Range(0, 4).Select(i => new ReturnEvidence(data.Return.Id, data.Owner,
                "https://example.test/" + i, null, DateTimeOffset.UnixEpoch))); await db.SaveChangesAsync();
        }
        var barrier = new StartTogether();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
        {
            await using var db = store.Context(barrier);
            return (await AsUser(new ReturnEvidenceController(db, TimeProvider.System), data.Owner).Add(data.Return.Number,
                new AddReturnEvidenceRequest("https://example.test/concurrent/" + i), default)).Result;
        })));
        Assert.Single(results, r => r is CreatedAtActionResult); Assert.Single(results, r => r is ConflictObjectResult);
        await using var fresh = store.Context(); Assert.Equal(5, await fresh.ReturnEvidence.CountAsync());
        var actual = await fresh.ReturnRequests.SingleAsync(); Assert.Equal((ReturnStatus.Requested, 19.44m), (actual.Status, actual.RefundAmount));
    }

    [Fact]
    public async Task Competing_tracking_transitions_save_one_parent_revision_and_exactly_one_event()
    {
        await using var store = new Store(); var data = await store.Seed(); var barrier = new StartTogether();
        var results = await Task.WhenAll(new[] { "InTransit", "Exception" }.Select(status => Task.Run(async () =>
        {
            await using var db = store.Context(barrier);
            return (await AsUser(new ShipmentTrackingController(db, TimeProvider.System), data.Actor).Add(data.Fulfillment.Id,
                new AddShipmentTrackingRequest(0, status), default)).Result;
        })));
        Assert.Single(results, r => r is CreatedAtActionResult); Assert.Single(results, r => r is ConflictObjectResult);
        await using var fresh = store.Context(); var shipment = await fresh.Fulfillments.SingleAsync(); var entry = await fresh.ShipmentTrackingEvents.SingleAsync();
        Assert.Equal(1L, shipment.TrackingVersion); Assert.Equal(1L, entry.Sequence); Assert.Equal(shipment.TrackingStatus, entry.Status);
        Assert.Equal(OrderStatus.Fulfilled, (await fresh.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Concurrent_return_creation_cannot_claim_the_same_remaining_quantities_twice()
    {
        await using var store = new Store(); var data = await store.Seed(); var barrier = new StartTogether(); var providers = new CountingCheckoutProviders();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var db = store.Context(barrier);
            var access = new GuestOrderAccessService(db, TimeProvider.System);
            var service = new ReturnService(db, providers, new ReturnEligibilityService(db, Options.Create(new ReturnPolicyOptions())), TimeProvider.System, access);
            try
            {
                await service.CreateAsync(new CreateReturnInput(data.Order.Number, ReturnReason.Damaged, null,
                    [new(data.Order.Items.Single().Id, 4)], new OrderAccessActor(data.Owner, false, null))); return "created";
            }
            catch (InvalidReturnRequestException) { return "exhausted"; }
        })));
        Assert.Single(results, r => r == "created"); Assert.Single(results, r => r == "exhausted");
        await using var fresh = store.Context(); Assert.Equal(2, await fresh.ReturnRequests.CountAsync());
        Assert.Equal(5, await fresh.ReturnRequestItems.SumAsync(i => i.Quantity)); Assert.Equal(0, providers.Refunds);
    }

    [Fact]
    public async Task Upgrade_preserves_history_without_inventing_tracking_and_child_cascades_preserve_actor_snapshots()
    {
        await using var store = new Store(); var data = await store.Seed(migrations: true);
        await using (var old = store.Context()) await old.GetService<IMigrator>().MigrateAsync("20260908213639_CheckoutPreferencesAndDiscountSchedules");
        await using (var upgraded = store.Context())
        {
            await upgraded.Database.MigrateAsync(); Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
            Assert.Empty(await upgraded.ReturnEvidence.ToListAsync()); Assert.Empty(await upgraded.OrderSupportNotes.ToListAsync()); Assert.Empty(await upgraded.ShipmentTrackingEvents.ToListAsync());
            var shipment = await upgraded.Fulfillments.SingleAsync();
            Assert.Equal((ShipmentTrackingStatus.Unknown, 0L, data.Fulfillment.CreatedAt), (shipment.TrackingStatus, shipment.TrackingVersion, shipment.CreatedAt));
            Assert.Equal((data.Order.Status, data.Order.Total), ((await upgraded.Orders.SingleAsync()).Status, (await upgraded.Orders.SingleAsync()).Total));
            Assert.Equal(data.Return.RefundAmount, (await upgraded.ReturnRequests.SingleAsync()).RefundAmount);
            upgraded.AddRange(new ReturnEvidence(data.Return.Id, data.Owner, "https://example.test/proof", null, DateTimeOffset.UtcNow),
                new OrderSupportNote(data.Order.Id, data.Actor, "Historical note", DateTimeOffset.UtcNow),
                shipment.RecordTracking(ShipmentTrackingStatus.InTransit, "Historical event", data.Actor, DateTimeOffset.UtcNow));
            await upgraded.SaveChangesAsync();
        }
        await using (var deletedActor = store.Context())
        {
            deletedActor.Customers.Remove(await deletedActor.Customers.SingleAsync(c => c.Id == data.Actor)); await deletedActor.SaveChangesAsync();
            Assert.Equal(data.Actor, (await deletedActor.OrderSupportNotes.SingleAsync()).AuthorAdminId);
            Assert.Equal(data.Actor, (await deletedActor.ShipmentTrackingEvents.SingleAsync()).ActorAdminId);
        }
        await using (var removed = store.Context())
        {
            removed.ReturnRequests.Remove(await removed.ReturnRequests.SingleAsync()); removed.Fulfillments.Remove(await removed.Fulfillments.SingleAsync());
            await removed.SaveChangesAsync(); Assert.Empty(await removed.ReturnEvidence.ToListAsync()); Assert.Empty(await removed.ShipmentTrackingEvents.ToListAsync());
            removed.Orders.Remove(await removed.Orders.SingleAsync()); await removed.SaveChangesAsync(); Assert.Empty(await removed.OrderSupportNotes.ToListAsync());
        }
    }

    private static T AsUser<T>(T controller, Guid owner) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", owner.ToString())], "Test")) } }; return controller;
    }
    private sealed class StartTogether : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource<bool> _both = new(TaskCreationOptions.RunContinuationsAsynchronously); private int _arrivals;
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            var arrival = Interlocked.Increment(ref _arrivals);
            if (arrival <= 2) { if (arrival == 2) _both.TrySetResult(true); await _both.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
            return result;
        }
    }
    private sealed record SeedData(Guid Owner, Guid Actor, Order Order, ReturnRequest Return, Fulfillment Fulfillment);
    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-operational-history-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=30");
            if (interceptor is not null) options.AddInterceptors(interceptor); return new AgoraDbContext(options.Options);
        }
        public async Task<SeedData> Seed(bool migrations = false)
        {
            await using var db = Context(); if (migrations) await db.Database.MigrateAsync(); else await db.Database.EnsureCreatedAsync();
            var owner = new Customer { Email = "history-owner@example.test", FullName = "Owner", PasswordHash = "unused-test-hash" };
            var actor = new Customer { Email = "history-actor@example.test", FullName = "Actor", PasswordHash = "unused-test-hash", Role = CustomerRole.Admin };
            var category = new Category { Name = "Category", Slug = "history-category" };
            var product = new Product { Name = "Historical product", Slug = "history-product", CategoryId = category.Id };
            var variant = new ProductVariant { ProductId = product.Id, Sku = "TEE-BLK-S", Name = "Variant", Price = new Money(20) };
            db.AddRange(owner, actor, category, product, variant, new InventoryItem(variant.Id, 100)); await db.SaveChangesAsync();
            var order = await OperationalHistoryTestData.Order(db, owner.Id, DateTimeOffset.UnixEpoch.AddDays(5));
            var returned = OperationalHistoryTestData.Return(order, 1, DateTimeOffset.UnixEpoch.AddDays(6));
            var shipment = OperationalHistoryTestData.Fulfillment(order, DateTimeOffset.UnixEpoch.AddDays(5));
            db.AddRange(returned, shipment); await db.SaveChangesAsync(); return new(owner.Id, actor.Id, order, returned, shipment);
        }
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
}
