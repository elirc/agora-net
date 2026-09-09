using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class CatalogEditingPersistenceTests
{
    [Fact]
    public async Task Upgrade_preserves_legacy_variant_values_and_gallery_rows_with_zero_revisions()
    {
        await using var store = new Store();
        Guid productId;
        Guid[] images;
        await using (var db = store.Context())
        {
            await db.Database.MigrateAsync();
            var product = await Seed(db);
            product.Variants[0].Name = new string('n', 180);
            await db.SaveChangesAsync();
            productId = product.Id;
            images = product.Images.OrderBy(i => i.SortOrder).Select(i => i.Id).ToArray();
            await db.GetService<IMigrator>().MigrateAsync("20260908192722_CatalogOrganization");
        }
        await using (var db = store.Context())
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var product = await db.Products.Include(p => p.Variants).Include(p => p.Images).SingleAsync(p => p.Id == productId);
            Assert.Equal(0, product.ImageRevision);
            Assert.Equal(images, product.Images.OrderBy(i => i.SortOrder).Select(i => i.Id));
            Assert.Equal(new[] { 3, 7 }, product.Images.OrderBy(i => i.SortOrder).Select(i => i.SortOrder));
            var variant = Assert.Single(product.Variants);
            Assert.Equal(0, variant.Version);
            Assert.Equal(new string('n', 180), variant.Name);
            Assert.Equal(10m, variant.Price.Amount);
            Assert.Equal("EUR", variant.Price.Currency);
            Assert.Equal(100, variant.WeightGrams);
            variant.Edit("Edited after upgrade", 12, 150, new Dictionary<string, string>());
            await db.SaveChangesAsync();
            Assert.Equal(1, variant.Version);
        }
    }

    [Fact]
    public async Task Competing_variant_save_keeps_the_winner_and_copies_the_callers_options()
    {
        await using var store = new Store();
        await using (var seed = store.Context()) { await seed.Database.EnsureCreatedAsync(); await Seed(seed); }
        await using var first = store.Context();
        await using var second = store.Context();
        var a = await first.ProductVariants.SingleAsync();
        var b = await second.ProductVariants.SingleAsync();
        var input = new Dictionary<string, string> { [" Size "] = " Large " };
        a.Edit("Winner", 24.50m, 750, input);
        input[" Size "] = "Mutated after edit";
        await first.SaveChangesAsync();
        b.Edit("Stale", 9, 50, new Dictionary<string, string>());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var fresh = store.Context();
        var actual = await fresh.ProductVariants.SingleAsync();
        Assert.Equal("Winner", actual.Name);
        Assert.Equal(24.50m, actual.Price.Amount);
        Assert.Equal("EUR", actual.Price.Currency);
        Assert.Equal("ORIGINAL", actual.Sku);
        Assert.Equal("Large", actual.Options["Size"]);
        Assert.Equal(1, actual.Version);
    }

    [Fact]
    public async Task Stale_gallery_reorder_rolls_back_child_positions_after_another_editor_adds_an_image()
    {
        await using var store = new Store();
        await using (var seed = store.Context()) { await seed.Database.EnsureCreatedAsync(); await Seed(seed); }
        await using var first = store.Context();
        await using var second = store.Context();
        var a = await first.Products.Include(p => p.Images).SingleAsync();
        var b = await second.Products.Include(p => p.Images).SingleAsync();
        var originalIds = a.Images.OrderBy(i => i.SortOrder).Select(i => i.Id).ToArray();
        var added = a.AddGalleryImage("https://example.test/new", null);
        first.ProductImages.Add(added);
        await first.SaveChangesAsync();
        b.ReplaceImageOrder(originalIds.Reverse().ToArray());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var fresh = store.Context();
        var actual = await fresh.Products.Include(p => p.Images).SingleAsync();
        Assert.Equal(originalIds.Append(added.Id), actual.Images.OrderBy(i => i.SortOrder).Select(i => i.Id));
        Assert.Equal(new[] { 0, 1, 2 }, actual.Images.OrderBy(i => i.SortOrder).Select(i => i.SortOrder));
        Assert.Equal(1, actual.ImageRevision);
    }

    private static async Task<Product> Seed(AgoraDbContext db)
    {
        var category = new Category { Name = "Category", Slug = "category" };
        var product = new Product { CategoryId = category.Id, Name = "Product", Slug = "product" };
        product.Variants.Add(new ProductVariant { ProductId = product.Id, Sku = "ORIGINAL", Name = "Original", Price = new Money(10, "EUR"), WeightGrams = 100 });
        foreach (var position in new[] { 3, 7 }) product.Images.Add(new ProductImage { ProductId = product.Id, Url = $"https://example.test/{position}", SortOrder = position });
        db.AddRange(category, product);
        await db.SaveChangesAsync();
        return product;
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-editing-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options);
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
}
