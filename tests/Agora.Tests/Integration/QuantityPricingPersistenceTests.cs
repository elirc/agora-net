using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class QuantityPricingPersistenceTests
{
    [Fact]
    public async Task Upgrade_leaves_legacy_prices_unchanged_and_adds_no_implicit_tiers()
    {
        await using var store = new Store();
        await using (var db = store.Context())
        {
            await db.Database.MigrateAsync();
            // Seed using the current model, then remove newer schema before testing the upgrade.
            var category = new Category { Name = "Old", Slug = "old" }; var product = new Product { CategoryId = category.Id, Name = "Old", Slug = "old" };
            product.Variants.Add(new ProductVariant { ProductId = product.Id, Name = "Old", Sku = "OLD-TIER", Price = new Money(12.34m) });
            db.AddRange(category, product); await db.SaveChangesAsync();
            await db.GetService<IMigrator>().MigrateAsync("20260908223533_CatalogImportStaging");
        }
        await using var upgraded = store.Context(); await upgraded.Database.MigrateAsync();
        Assert.Equal(12.34m, (await upgraded.ProductVariants.SingleAsync()).Price.Amount);
        Assert.Empty(await upgraded.Set<VariantQuantityPricing>().ToListAsync()); Assert.Empty(await upgraded.Set<VariantQuantityTier>().ToListAsync());
        Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Stale_replacement_rolls_back_tier_changes_and_existing_thresholds_can_be_reused()
    {
        await using var store = new Store(); Guid id;
        await using (var seed = store.Context())
        {
            await seed.Database.EnsureCreatedAsync();
            var category = new Category { Name = "Race", Slug = "race" }; var product = new Product { CategoryId = category.Id, Name = "Race", Slug = "race" };
            var variant = new ProductVariant { ProductId = product.Id, Sku = "RACE-TIER", Price = new Money(10) }; id = variant.Id;
            product.Variants.Add(variant); seed.AddRange(category, product, new VariantQuantityPricing(id, [new(5, 9), new(10, 8)], 10)); await seed.SaveChangesAsync();
        }
        await using var first = store.Context(); await using var second = store.Context();
        var a = await first.Set<VariantQuantityPricing>().Include(p => p.Tiers).SingleAsync();
        var b = await second.Set<VariantQuantityPricing>().Include(p => p.Tiers).SingleAsync();
        a.Replace([new(5, 8), new(20, 6)], 10); b.Replace([new(3, 7)], 10);
        await first.SaveChangesAsync(); await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var fresh = store.Context(); var policy = await fresh.Set<VariantQuantityPricing>().Include(p => p.Tiers).SingleAsync();
        Assert.Equal(1L, policy.Revision); Assert.Equal(new[] { (5, 8m), (20, 6m) }, policy.Tiers.OrderBy(t => t.MinimumQuantity).Select(t => (t.MinimumQuantity, t.UnitAmount)));
        fresh.ProductVariants.Remove(await fresh.ProductVariants.SingleAsync(v => v.Id == id)); await fresh.SaveChangesAsync();
        Assert.Empty(await fresh.Set<VariantQuantityPricing>().ToListAsync()); Assert.Empty(await fresh.Set<VariantQuantityTier>().ToListAsync());
    }
    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-tiers-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options);
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
}
