using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public sealed class WarehouseCoordinationPersistenceTests
{
    [Fact]
    public async Task Upgrade_keeps_existing_order_and_starts_coordination_empty()
    {
        await using var store = new Store();
        await using (var latest = store.Context()) await latest.Database.MigrateAsync();
        var order = await store.SeedOrder();
        await using (var downgrade = store.Context())
            await downgrade.GetService<IMigrator>().MigrateAsync("20260908224638_SellingWarehouseAndAccessPolicies");
        await using var upgraded = store.Context();
        await upgraded.Database.MigrateAsync();
        Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
        Assert.Equal(OrderStatus.PartiallyFulfilled,
            (await upgraded.Orders.SingleAsync(x => x.Id == order.Id)).Status);
        Assert.Empty(await upgraded.Set<OrderHold>().ToArrayAsync());
        Assert.Empty(await upgraded.Set<WarehouseAssignment>().ToArrayAsync());
    }

    [Fact]
    public async Task Independent_hold_creates_leave_one_active_winner()
    {
        await using var store = new Store();
        var order = await store.SeedOrder();
        async Task<Exception?> Attempt() => await Record.ExceptionAsync(async () =>
        {
            await using var db = store.Context();
            await new OrderHoldService(db, TimeProvider.System).CreateAsync(
                order.Number, OrderHoldReason.StockInvestigation, null, Guid.NewGuid(), default);
        });

        var outcomes = await Task.WhenAll(Task.Run(Attempt), Task.Run(Attempt));
        Assert.Single(outcomes, error => error is null);
        Assert.Single(outcomes, error => error is not null);
        await using var verify = store.Context();
        Assert.Single(await verify.Set<OrderHold>().Where(x => x.IsActive).ToArrayAsync());
        Assert.Equal(OrderStatus.PartiallyFulfilled,
            (await verify.Orders.SingleAsync(x => x.Id == order.Id)).Status);
    }

    [Fact]
    public async Task Concurrent_hold_and_fulfillment_produce_one_valid_serial_order()
    {
        await using var store = new Store();
        var order = await store.SeedOrder();
        var actor = Guid.NewGuid();
        long stockBefore;
        await using (var baseline = store.Context())
            stockBefore = await baseline.InventoryItems.SumAsync(x => (long)x.QuantityOnHand);

        async Task<Exception?> Hold() => await Record.ExceptionAsync(async () =>
        {
            await using var db = store.Context();
            await new OrderHoldService(db, TimeProvider.System).CreateAsync(
                order.Number, OrderHoldReason.CustomerRequest, null, actor, default);
        });
        async Task<Exception?> Fulfill() => await Record.ExceptionAsync(async () =>
        {
            await using var db = store.Context();
            var webhook = new WebhookService(db, TimeProvider.System);
            await new FulfillmentService(db, webhook, TimeProvider.System).CreateAsync(
                order.Number, null, null, null, default, assignmentId: null, actorId: actor);
        });

        var outcomes = await Task.WhenAll(Task.Run(Hold), Task.Run(Fulfill));
        Assert.Single(outcomes, error => error is null);
        Assert.Single(outcomes, error => error is not null);

        await using var verify = store.Context();
        var activeHold = await verify.Set<OrderHold>().AnyAsync(x => x.OrderId == order.Id && x.IsActive);
        var shipment = await verify.Fulfillments.AnyAsync(x => x.OrderId == order.Id);
        Assert.NotEqual(activeHold, shipment);
        Assert.Equal(activeHold ? OrderStatus.PartiallyFulfilled : OrderStatus.Fulfilled,
            (await verify.Orders.SingleAsync(x => x.Id == order.Id)).Status);
        Assert.Equal(stockBefore, await verify.InventoryItems.SumAsync(x => (long)x.QuantityOnHand));
    }

    [Fact]
    public async Task Fulfillment_first_remains_valid_when_later_hold_is_rejected()
    {
        await using var store = new Store();
        var order = await store.SeedOrder();
        await using (var ship = store.Context())
        {
            var service = new FulfillmentService(ship,
                new WebhookService(ship, TimeProvider.System), TimeProvider.System);
            await service.CreateAsync(order.Number, null, null, null, default,
                assignmentId: null, actorId: Guid.NewGuid());
        }
        await using (var hold = store.Context())
        {
            await Assert.ThrowsAsync<WarehouseCoordinationConflictException>(() =>
                new OrderHoldService(hold, TimeProvider.System).CreateAsync(order.Number,
                    OrderHoldReason.AddressQuestion, null, Guid.NewGuid(), default));
        }
        await using var verify = store.Context();
        Assert.Single(await verify.Fulfillments.Where(x => x.OrderId == order.Id).ToArrayAsync());
        Assert.Empty(await verify.Set<OrderHold>().ToArrayAsync());
        Assert.Equal(OrderStatus.Fulfilled,
            (await verify.Orders.SingleAsync(x => x.Id == order.Id)).Status);
    }

    [Fact]
    public async Task Independent_claims_leave_one_live_owner_and_generation()
    {
        await using var store = new Store();
        var order = await store.SeedOrder();
        async Task<Exception?> Attempt() => await Record.ExceptionAsync(async () =>
        {
            await using var db = store.Context();
            await new WarehouseAssignmentService(db, TimeProvider.System)
                .ClaimAsync(order.Number, Guid.NewGuid(), default);
        });

        var outcomes = await Task.WhenAll(Task.Run(Attempt), Task.Run(Attempt));
        Assert.Single(outcomes, error => error is null);
        Assert.Single(outcomes, error => error is not null);
        await using var verify = store.Context();
        var slot = Assert.Single(await verify.Set<WarehouseAssignment>().ToArrayAsync());
        Assert.NotEqual(Guid.Empty, slot.AssignmentId);
    }

    [Fact]
    public async Task Exact_expiry_replaces_assignment_and_old_generation_stays_invalid()
    {
        await using var store = new Store();
        var order = await store.SeedOrder();
        var clock = new MutableClock(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        Guid oldId;

        await using (var first = store.Context())
        {
            var old = await new WarehouseAssignmentService(first, clock)
                .ClaimAsync(order.Number, Guid.NewGuid(), default);
            oldId = old.AssignmentId;
        }
        clock.Now = clock.Now.AddMinutes(15);
        await using (var second = store.Context())
        {
            var replacement = await new WarehouseAssignmentService(second, clock)
                .ClaimAsync(order.Number, Guid.NewGuid(), default);
            Assert.NotEqual(oldId, replacement.AssignmentId);
            Assert.Equal(2, replacement.Revision);
        }
    }

    [Fact]
    public async Task Expired_assignment_id_cannot_authorize_fulfillment()
    {
        await using var store = new Store();
        var order = await store.SeedOrder();
        var clock = new MutableClock(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        Guid assignmentId;
        Guid owner;
        await using (var claim = store.Context())
        {
            owner = Guid.NewGuid();
            assignmentId = (await new WarehouseAssignmentService(claim, clock)
                .ClaimAsync(order.Number, owner, default)).AssignmentId;
        }
        clock.Now = clock.Now.AddMinutes(15);
        await using (var attempt = store.Context())
        {
            var service = new FulfillmentService(attempt,
                new WebhookService(attempt, clock), clock);
            await Assert.ThrowsAsync<WarehouseCoordinationConflictException>(() => service.CreateAsync(
                order.Number, null, null, null, default, assignmentId, owner));
        }
        await using var verify = store.Context();
        Assert.Empty(await verify.Fulfillments.Where(x => x.OrderId == order.Id).ToArrayAsync());
    }

    [Fact]
    public async Task Cancelled_order_cannot_be_claimed()
    {
        await using var store = new Store();
        var order = await store.SeedOrder();
        await using (var cancel = store.Context())
        {
            await cancel.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Orders SET Status = {(int)OrderStatus.Cancelled} WHERE Id = {order.Id}");
        }
        await using var claim = store.Context();
        await Assert.ThrowsAsync<WarehouseCoordinationConflictException>(() =>
            new WarehouseAssignmentService(claim, TimeProvider.System)
                .ClaimAsync(order.Number, Guid.NewGuid(), default));
        Assert.Empty(await claim.Set<WarehouseAssignment>().ToArrayAsync());
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"agora-coordination-{Guid.NewGuid():N}.db");
        public AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=30").Options);
        public async Task<Order> SeedOrder(string? targetMigration = null)
        {
            await using var db = Context();
            if (targetMigration is null) await db.Database.EnsureCreatedAsync();
            else await db.GetService<IMigrator>().MigrateAsync(targetMigration);
            await AgoraDbSeeder.SeedAsync(db);
            return await OperationalHistoryTestData.Order(db, null, DateTimeOffset.UtcNow,
                quantity: 3, fulfilled: false);
        }
        public ValueTask DisposeAsync() { File.Delete(path); return ValueTask.CompletedTask; }
    }
}
