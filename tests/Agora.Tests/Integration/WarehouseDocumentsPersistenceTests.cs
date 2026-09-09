using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public sealed class WarehouseDocumentsPersistenceTests
{
    [Fact]
    public async Task Upgrade_preserves_existing_stock_and_starts_warehouse_documents_empty()
    {
        await using var store = new WarehouseStore();
        await using (var latest = store.Context()) await latest.Database.MigrateAsync();
        var seed = await store.SeedAsync();
        await using (var downgrade = store.Context())
            await downgrade.GetService<IMigrator>().MigrateAsync("20260908223533_CatalogImportStaging");

        await using var upgraded = store.Context();
        await upgraded.Database.MigrateAsync();

        Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
        Assert.Equal(seed.AOnHand,
            (await upgraded.InventoryItems.SingleAsync(x => x.ProductVariantId == seed.A)).QuantityOnHand);
        Assert.Empty(await upgraded.Set<Supplier>().ToArrayAsync());
        Assert.Empty(await upgraded.Set<PurchaseOrder>().ToArrayAsync());
        Assert.Empty(await upgraded.Set<PurchaseOrderReceipt>().ToArrayAsync());
        Assert.Empty(await upgraded.Set<InventoryCountSession>().ToArrayAsync());
    }

    [Fact]
    public async Task Independent_final_receipts_have_one_winner()
    {
        await using var store = new WarehouseStore();
        var seed = await store.SeedAsync();
        Guid poId;
        Guid lineId;

        await using (var arrange = store.Context())
        {
            var service = new PurchaseOrderService(arrange, TimeProvider.System);
            var supplier = await service.CreateSupplierAsync("Race supplier", null, default);
            var po = await service.CreateAsync(supplier.Id, [(seed.A, 5)], default);
            po = await service.SubmitAsync(po.Id, 0, default);
            poId = po.Id;
            lineId = po.Lines.Single().Id;
        }

        async Task<Exception?> Attempt(Guid operationId) => await Record.ExceptionAsync(async () =>
        {
            await using var context = store.Context();
            var service = new PurchaseOrderService(context, TimeProvider.System);
            await service.ReceiveAsync(poId, operationId, 1, Guid.NewGuid(), [new(lineId, 5)], default);
        });

        var attempts = await Task.WhenAll(
            Task.Run(() => Attempt(Guid.NewGuid())),
            Task.Run(() => Attempt(Guid.NewGuid())));

        Assert.Single(attempts, error => error is null);
        Assert.Single(attempts, error => error is not null);
        await using var verify = store.Context();
        Assert.Single(await verify.Set<PurchaseOrderReceipt>().ToArrayAsync());
        Assert.Equal(seed.AOnHand + 5,
            (await verify.InventoryItems.SingleAsync(x => x.ProductVariantId == seed.A)).QuantityOnHand);
        Assert.Equal(PurchaseOrderStatus.Received,
            (await verify.Set<PurchaseOrder>().SingleAsync(x => x.Id == poId)).Status);
    }

    [Fact]
    public async Task Receipt_line_failure_rolls_back_document_receipt_and_stock()
    {
        await using var store = new WarehouseStore();
        var seed = await store.SeedAsync();
        Guid poId;
        Guid lineId;

        await using (var arrange = store.Context())
        {
            var service = new PurchaseOrderService(arrange, TimeProvider.System);
            var supplier = await service.CreateSupplierAsync("Rollback supplier", null, default);
            var po = await service.CreateAsync(supplier.Id, [(seed.A, 5)], default);
            await service.SubmitAsync(po.Id, 0, default);
            poId = po.Id;
            lineId = po.Lines.Single().Id;
            await arrange.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER RejectPurchaseOrderReceiptLine
                AFTER INSERT ON PurchaseOrderReceiptLines
                BEGIN SELECT RAISE(ABORT, 'injected receipt failure'); END;
                """);
        }

        await using (var failing = store.Context())
        {
            var service = new PurchaseOrderService(failing, TimeProvider.System);
            await Assert.ThrowsAsync<DbUpdateException>(() => service.ReceiveAsync(
                poId, Guid.NewGuid(), 1, Guid.NewGuid(), [new(lineId, 5)], default));
        }

        await using var verify = store.Context();
        var poAfter = await verify.Set<PurchaseOrder>().Include(x => x.Lines).SingleAsync(x => x.Id == poId);
        var stockAfter = await verify.InventoryItems.SingleAsync(x => x.ProductVariantId == seed.A);
        Assert.Equal(PurchaseOrderStatus.Ordered, poAfter.Status);
        Assert.Equal(0, poAfter.Lines.Single().ReceivedQuantity);
        Assert.Equal(seed.AOnHand, stockAfter.QuantityOnHand);
        Assert.Empty(await verify.Set<PurchaseOrderReceipt>().ToArrayAsync());
    }

    [Fact]
    public async Task One_stale_count_line_rejects_every_other_valid_line()
    {
        await using var store = new WarehouseStore();
        var seed = await store.SeedAsync();
        Guid sessionId;
        long revision;

        await using (var arrange = store.Context())
        {
            var service = new InventoryCountService(arrange, TimeProvider.System);
            var session = await service.CreateAsync(Guid.NewGuid(), [seed.A, seed.B], default);
            foreach (var line in session.Lines.OrderBy(x => x.Sku))
            {
                session = await service.RecordAsync(session.Id, line.Id, line.BaselineOnHand - 1, session.Revision, default);
            }
            sessionId = session.Id;
            revision = session.Revision;
        }

        await using (var competing = store.Context())
        {
            var stock = await competing.InventoryItems.SingleAsync(x => x.ProductVariantId == seed.A);
            stock.Restock(1);
            await competing.SaveChangesAsync();
        }

        await using (var apply = store.Context())
        {
            var service = new InventoryCountService(apply, TimeProvider.System);
            await Assert.ThrowsAsync<InventoryCountConflictException>(() =>
                service.ApplyAsync(sessionId, Guid.NewGuid(), revision, default));
        }

        await using var verify = store.Context();
        Assert.Equal(seed.AOnHand + 1, (await verify.InventoryItems.SingleAsync(x => x.ProductVariantId == seed.A)).QuantityOnHand);
        Assert.Equal(seed.BOnHand, (await verify.InventoryItems.SingleAsync(x => x.ProductVariantId == seed.B)).QuantityOnHand);
        Assert.Equal(InventoryCountStatus.Open, (await verify.Set<InventoryCountSession>().SingleAsync(x => x.Id == sessionId)).Status);
    }

    [Fact]
    public async Task Deleted_variant_preserves_count_history_but_blocks_open_session()
    {
        await using var store = new WarehouseStore();
        var seed = await store.SeedAsync();
        Guid sessionId;
        long revision;

        await using (var arrange = store.Context())
        {
            var service = new InventoryCountService(arrange, TimeProvider.System);
            var session = await service.CreateAsync(Guid.NewGuid(), [seed.A], default);
            session = await service.RecordAsync(session.Id, session.Lines.Single().Id, seed.AOnHand, 0, default);
            sessionId = session.Id;
            revision = session.Revision;
        }

        await using (var delete = store.Context())
        {
            delete.ProductVariants.Remove(await delete.ProductVariants.SingleAsync(x => x.Id == seed.A));
            await delete.SaveChangesAsync();
        }

        await using (var apply = store.Context())
        {
            var service = new InventoryCountService(apply, TimeProvider.System);
            await Assert.ThrowsAsync<InventoryCountConflictException>(() =>
                service.ApplyAsync(sessionId, Guid.NewGuid(), revision, default));
        }

        await using var verify = store.Context();
        var historical = await verify.Set<InventoryCountLine>().SingleAsync(x => x.SessionId == sessionId);
        Assert.Null(historical.ProductVariantId);
        Assert.Equal("WARE-A", historical.Sku);
    }

    [Fact]
    public async Task Deleted_variant_preserves_purchase_order_line_but_blocks_receipt()
    {
        await using var store = new WarehouseStore();
        var seed = await store.SeedAsync();
        Guid poId;
        Guid lineId;

        await using (var arrange = store.Context())
        {
            var service = new PurchaseOrderService(arrange, TimeProvider.System);
            var supplier = await service.CreateSupplierAsync("Historical supplier", null, default);
            var po = await service.CreateAsync(supplier.Id, [(seed.A, 2)], default);
            po = await service.SubmitAsync(po.Id, 0, default);
            poId = po.Id;
            lineId = po.Lines.Single().Id;
        }

        await using (var delete = store.Context())
        {
            delete.ProductVariants.Remove(await delete.ProductVariants.SingleAsync(x => x.Id == seed.A));
            await delete.SaveChangesAsync();
        }

        await using (var receive = store.Context())
        {
            var service = new PurchaseOrderService(receive, TimeProvider.System);
            await Assert.ThrowsAsync<InvalidProcurementException>(() => service.ReceiveAsync(
                poId, Guid.NewGuid(), 1, Guid.NewGuid(), [new(lineId, 1)], default));
        }

        await using var verify = store.Context();
        var historical = await verify.Set<PurchaseOrderLine>().SingleAsync(x => x.Id == lineId);
        Assert.Null(historical.ProductVariantId);
        Assert.Equal("WARE-A", historical.Sku);
        Assert.Empty(await verify.Set<PurchaseOrderReceipt>().ToArrayAsync());
    }

    private sealed record Seed(Guid A, Guid B, int AOnHand, int BOnHand);

    private sealed class WarehouseStore : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"agora-warehouse-{Guid.NewGuid():N}.db");
        public AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=30").Options);

        public async Task<Seed> SeedAsync(string? targetMigration = null)
        {
            await using var db = Context();
            if (targetMigration is null)
            {
                await db.Database.EnsureCreatedAsync();
            }
            else
            {
                await db.GetService<IMigrator>().MigrateAsync(targetMigration);
            }
            var category = new Category { Name = "Warehouse", Slug = $"warehouse-{Guid.NewGuid():N}" };
            var product = new Product { Name = "Warehouse product", Slug = $"warehouse-product-{Guid.NewGuid():N}", CategoryId = category.Id };
            var a = new ProductVariant { ProductId = product.Id, Name = "A", Sku = "WARE-A", Price = new Money(1) };
            var b = new ProductVariant { ProductId = product.Id, Name = "B", Sku = "WARE-B", Price = new Money(1) };
            a.Inventory = new InventoryItem(a.Id, 10);
            b.Inventory = new InventoryItem(b.Id, 8);
            product.Variants.AddRange([a, b]);
            db.AddRange(category, product);
            await db.SaveChangesAsync();
            return new Seed(a.Id, b.Id, 10, 8);
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(path);
            return ValueTask.CompletedTask;
        }
    }
}
