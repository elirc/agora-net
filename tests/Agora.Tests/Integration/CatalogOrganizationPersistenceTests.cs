using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class CatalogOrganizationPersistenceTests
{
    [Fact]
    public async Task Upgrade_preserves_existing_products_with_no_tags_and_zero_tag_version()
    {
        await using var store = new Store();
        Guid id;
        await using (var db = store.Context())
        {
            await db.Database.MigrateAsync();
            id = (await Seed(db))[0].Id;
            await db.GetService<IMigrator>().MigrateAsync("20260908190324_WishlistNotesAndMembership");
        }
        await using (var db = store.Context())
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var product = await db.Products.Include(p => p.Tags).SingleAsync(p => p.Id == id);
            Assert.Equal("Product A", product.Name);
            Assert.Equal(0, product.TagVersion);
            Assert.Empty(product.Tags);
            Assert.Empty(await db.Tags.ToListAsync());
            Assert.Empty(await db.ProductCollections.ToListAsync());
        }
    }

    [Fact]
    public async Task Stale_tag_replacement_rolls_back_its_membership_changes()
    {
        await using var store = new Store();
        var a = new Tag("A", "a");
        var b = new Tag("B", "b");
        Guid productId;
        await using (var seed = store.Context())
        {
            await seed.Database.EnsureCreatedAsync();
            productId = (await Seed(seed))[0].Id;
            seed.Tags.AddRange(a, b);
            await seed.SaveChangesAsync();
        }
        await using var first = store.Context();
        await using var second = store.Context();
        var one = await first.Products.Include(p => p.Tags).SingleAsync(p => p.Id == productId);
        var two = await second.Products.Include(p => p.Tags).SingleAsync(p => p.Id == productId);
        one.ReplaceTags([a.Id]);
        first.ProductTags.AddRange(one.Tags);
        await first.SaveChangesAsync();
        two.ReplaceTags([b.Id]);
        second.ProductTags.AddRange(two.Tags);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var fresh = store.Context();
        var actual = await fresh.Products.Include(p => p.Tags).SingleAsync(p => p.Id == productId);
        Assert.Equal(a.Id, Assert.Single(actual.Tags).TagId);
        Assert.Equal(1, actual.TagVersion);
    }

    [Fact]
    public async Task Competing_collection_reorder_cannot_overwrite_the_winning_order()
    {
        await using var store = new Store();
        Guid id;
        Guid[] ids;
        await using (var seed = store.Context())
        {
            await seed.Database.EnsureCreatedAsync();
            ids = (await Seed(seed)).Select(p => p.Id).ToArray();
            var collection = new ProductCollection("Collection", "collection");
            collection.Replace("Collection", true, ids);
            seed.ProductCollections.Add(collection);
            await seed.SaveChangesAsync();
            id = collection.Id;
        }
        await using var first = store.Context();
        await using var second = store.Context();
        var a = await first.ProductCollections.Include(c => c.Items).SingleAsync(c => c.Id == id);
        var b = await second.ProductCollections.Include(c => c.Items).SingleAsync(c => c.Id == id);
        var reversed = ids.Reverse().ToArray();
        a.Replace("Winner", true, reversed);
        await first.SaveChangesAsync();
        b.Replace("Losing editor", false, ids);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var fresh = store.Context();
        var actual = await fresh.ProductCollections.Include(c => c.Items).SingleAsync();
        Assert.Equal(reversed, actual.Items.OrderBy(i => i.Position).Select(i => i.ProductId));
        Assert.Equal("Winner", actual.Title);
        Assert.True(actual.IsPublished);
        Assert.Equal(2, actual.Version);
    }

    private static async Task<Product[]> Seed(AgoraDbContext db)
    {
        var category = new Category { Name = "Category", Slug = "category" };
        var products = new[] { "A", "B" }.Select(key => new Product { CategoryId = category.Id, Name = "Product " + key, Slug = "product-" + key }).ToArray();
        foreach (var product in products) product.Variants.Add(new ProductVariant { ProductId = product.Id, Sku = product.Slug, Name = "Choice", Price = new Money(10) });
        db.Categories.Add(category);
        db.Products.AddRange(products);
        await db.SaveChangesAsync();
        return products;
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-catalog-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options);
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
}
