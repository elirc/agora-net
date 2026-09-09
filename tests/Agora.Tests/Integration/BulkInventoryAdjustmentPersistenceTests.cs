using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace Agora.Tests.Integration;

public class BulkInventoryAdjustmentPersistenceTests
{
    [Fact]
    public async Task Independent_concurrent_requests_with_one_operation_apply_once_and_return_one_receipt()
    {
        await using var store = new Store(coordinateInitialReads: true);
        var command = await store.Seed();
        var actor = Guid.NewGuid();
        var calls = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            using var scope = store.Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<InventoryAdjustmentService>().ApplyAsync(actor, command);
        })).ToArray();
        var results = await Task.WhenAll(calls);
        Assert.Single(results, r => r.Replayed);
        Assert.Single(results, r => !r.Replayed);
        Assert.All(results, r => Assert.Equal(command.OperationId, r.Receipt.Id));
        await using var db = store.Context();
        Assert.Equal(1, await db.InventoryAdjustmentBatches.CountAsync());
        Assert.Equal(2, await db.InventoryAdjustmentLines.CountAsync());
        Assert.Equal(7, (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == command.Lines[0].VariantId)).QuantityOnHand);
        Assert.Equal(12, (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == command.Lines[1].VariantId)).QuantityOnHand);
        var receipt = await db.InventoryAdjustmentBatches.SingleAsync();
        Assert.Equal(actor, receipt.ActorId);
        using var replayScope = store.Provider.CreateScope();
        var changed = InventoryAdjustmentCommand.Create(command.OperationId, "Different reason", command.Lines);
        await Assert.ThrowsAsync<InventoryAdjustmentConflictException>(() => replayScope.ServiceProvider.GetRequiredService<InventoryAdjustmentService>().ApplyAsync(actor, changed));
    }

    [Fact]
    public async Task Database_failure_after_parent_insert_rolls_back_receipt_and_every_stock_balance()
    {
        await using var store = new Store();
        var command = await store.Seed();
        await using (var arrange = store.Context())
            await arrange.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER RejectReceiptLine AFTER INSERT ON InventoryAdjustmentLines
                BEGIN SELECT RAISE(ABORT, 'injected receipt persistence failure'); END;
                """);
        using (var scope = store.Provider.CreateScope())
            await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<InventoryAdjustmentService>().ApplyAsync(Guid.NewGuid(), command));
        await using (var fresh = store.Context())
        {
            Assert.Empty(await fresh.InventoryAdjustmentBatches.ToListAsync());
            Assert.Empty(await fresh.InventoryAdjustmentLines.ToListAsync());
            var a = await fresh.InventoryItems.SingleAsync(i => i.ProductVariantId == command.Lines[0].VariantId);
            var b = await fresh.InventoryItems.SingleAsync(i => i.ProductVariantId == command.Lines[1].VariantId);
            Assert.Equal((10, 8), (a.QuantityOnHand, b.QuantityOnHand));
            Assert.Equal(command.Lines[0].ExpectedVersion, a.Version);
            Assert.Equal(command.Lines[1].ExpectedVersion, b.Version);
            await fresh.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectReceiptLine");
        }
        using var retry = store.Provider.CreateScope();
        var result = await retry.ServiceProvider.GetRequiredService<InventoryAdjustmentService>().ApplyAsync(Guid.NewGuid(), command);
        Assert.False(result.Replayed);
    }

    [Fact]
    public async Task Upgrade_keeps_existing_stock_and_policy_and_starts_receipts_empty()
    {
        await using var store = new Store();
        var command = await store.Seed(migrations: true);
        await using (var old = store.Context())
        {
            old.InventoryReorderPolicies.Add(new InventoryReorderPolicy(command.Lines[0].VariantId, 8, 20, DateTimeOffset.UtcNow));
            await old.SaveChangesAsync();
            await old.GetService<IMigrator>().MigrateAsync("20260908203717_InventoryReorderPolicies");
        }
        await using (var upgraded = store.Context())
        {
            await upgraded.Database.MigrateAsync();
            Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
            Assert.Empty(await upgraded.InventoryAdjustmentBatches.ToListAsync());
            Assert.Empty(await upgraded.InventoryAdjustmentLines.ToListAsync());
            Assert.Equal(20, (await upgraded.InventoryReorderPolicies.SingleAsync()).TargetLevel);
            var stock = await upgraded.InventoryItems.SingleAsync(i => i.ProductVariantId == command.Lines[0].VariantId);
            Assert.Equal((10, 2, 1), (stock.QuantityOnHand, stock.QuantityReserved, stock.Version));
        }
        using var scope = store.Provider.CreateScope();
        Assert.False((await scope.ServiceProvider.GetRequiredService<InventoryAdjustmentService>().ApplyAsync(Guid.NewGuid(), command)).Replayed);
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-batch-" + Guid.NewGuid().ToString("N") + ".db");
        public ServiceProvider Provider { get; }
        public Store(bool coordinateInitialReads = false)
        {
            var barrier = coordinateInitialReads ? new InitialReceiptReadBarrier() : null;
            Provider = new ServiceCollection().AddDbContext<AgoraDbContext>(options =>
                {
                    options.UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=30");
                    if (barrier is not null) options.AddInterceptors(barrier);
                })
                .AddSingleton(TimeProvider.System).AddScoped<InventoryAdjustmentService>().BuildServiceProvider();
        }
        public AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=30").Options);
        public async Task<InventoryAdjustmentCommand> Seed(bool migrations = false)
        {
            await using var db = Context();
            if (migrations) await db.Database.MigrateAsync(); else await db.Database.EnsureCreatedAsync();
            var category = new Category { Name = "Batch", Slug = "batch" };
            var product = new Product { Name = "Batch product", Slug = "batch-product", CategoryId = category.Id };
            var a = new ProductVariant { Id = Guid.Parse("10000000-0000-0000-0000-000000000001"), ProductId = product.Id, Name = "A", Sku = "BATCH-A", Price = new Money(10) };
            var b = new ProductVariant { Id = Guid.Parse("20000000-0000-0000-0000-000000000002"), ProductId = product.Id, Name = "B", Sku = "BATCH-B", Price = new Money(10) };
            a.Inventory = new InventoryItem(a.Id, 10); a.Inventory.Reserve(2);
            b.Inventory = new InventoryItem(b.Id, 8); product.Variants.AddRange([a, b]);
            db.AddRange(category, product); await db.SaveChangesAsync();
            return InventoryAdjustmentCommand.Create(Guid.NewGuid(), "Cycle count", [new(a.Id, -3, a.Inventory.Version), new(b.Id, 4, b.Inventory.Version)]);
        }
        public async ValueTask DisposeAsync() { await Provider.DisposeAsync(); File.Delete(_path); }
    }

    private sealed class InitialReceiptReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _seen;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"InventoryAdjustmentBatches\"", StringComparison.Ordinal))
            {
                var arrival = Interlocked.Increment(ref _seen);
                if (arrival <= 2)
                {
                    if (arrival == 2) _both.TrySetResult(true);
                    await _both.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
            return result;
        }
    }
}
