using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class ReorderPolicyPersistenceTests
{
    [Fact]
    public async Task Upgrade_preserves_stock_and_does_not_materialize_default_policies()
    {
        await using var store = new Store();
        Guid variantId;
        await using (var db = store.Context())
        {
            await db.Database.MigrateAsync();
            variantId = await Seed(db);
            await db.GetService<IMigrator>().MigrateAsync("20260908195106_VariantAndGalleryRevisions");
        }
        await using (var db = store.Context())
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Empty(await db.InventoryReorderPolicies.ToListAsync());
            var stock = await db.InventoryItems.SingleAsync(i => i.ProductVariantId == variantId);
            Assert.Equal((12, 4, 1), (stock.QuantityOnHand, stock.QuantityReserved, stock.Version));
            db.InventoryReorderPolicies.Add(new InventoryReorderPolicy(variantId, 8, 20, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Independent_writers_enforce_both_unique_creation_and_revision_updates_and_cascade_deletion()
    {
        await using var store = new Store();
        Guid id;
        await using (var seed = store.Context()) { await seed.Database.EnsureCreatedAsync(); id = await Seed(seed); }
        await using var a = store.Context();
        await using var b = store.Context();
        Assert.False(await a.InventoryReorderPolicies.AnyAsync());
        Assert.False(await b.InventoryReorderPolicies.AnyAsync());
        a.Add(new InventoryReorderPolicy(id, 8, 20, DateTimeOffset.UtcNow));
        b.Add(new InventoryReorderPolicy(id, 9, 30, DateTimeOffset.UtcNow));
        await a.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateException>(() => b.SaveChangesAsync());
        b.ChangeTracker.Clear();
        var winner = await a.InventoryReorderPolicies.SingleAsync();
        var stale = await b.InventoryReorderPolicies.SingleAsync();
        winner.Replace(10, 40, DateTimeOffset.UtcNow); await a.SaveChangesAsync();
        stale.Replace(15, 50, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
        await using var fresh = store.Context();
        var actual = await fresh.InventoryReorderPolicies.SingleAsync();
        Assert.Equal((10, 40, 1L), (actual.Threshold, actual.TargetLevel, actual.Version));
        fresh.ProductVariants.Remove(await fresh.ProductVariants.SingleAsync());
        await fresh.SaveChangesAsync();
        Assert.Empty(await fresh.InventoryReorderPolicies.ToListAsync());
    }

    [Fact]
    public void Invalid_replacement_preserves_the_previous_policy()
    {
        var time = DateTimeOffset.UnixEpoch;
        var policy = new InventoryReorderPolicy(Guid.NewGuid(), 8, 20, time);
        Assert.Throws<DomainException>(() => policy.Replace(21, 20, time.AddDays(1)));
        Assert.Equal((8, 20, 0L, time), (policy.Threshold, policy.TargetLevel, policy.Version, policy.UpdatedAt));
    }

    private static async Task<Guid> Seed(AgoraDbContext db)
    {
        var category = new Category { Name = "Stock", Slug = "stock" };
        var product = new Product { Name = "Stock item", Slug = "stock-item", CategoryId = category.Id };
        var variant = new ProductVariant { ProductId = product.Id, Name = "Stock variant", Sku = "STOCK", Price = new Money(10) };
        variant.Inventory = new InventoryItem(variant.Id, 12); variant.Inventory.Reserve(4);
        product.Variants.Add(variant); db.AddRange(category, product); await db.SaveChangesAsync();
        return variant.Id;
    }
    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-reorder-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options);
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
}
