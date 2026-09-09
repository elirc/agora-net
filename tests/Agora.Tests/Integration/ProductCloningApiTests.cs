using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class ProductCloningApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private static string Key() => Guid.NewGuid().ToString("N");
    private async Task<HttpClient> Admin()
    {
        var client = factory.CreateClient();
        await client.AuthenticateAsAdminAsync();
        return client;
    }
    private async Task<Product> Source(int variantCount = 2)
    {
        var category = new Category { Name = "Clone category", Slug = Key() };
        var tax = new TaxCategory { Code = Key(), Name = "Clone tax classification" };
        var product = new Product { CategoryId = category.Id, TaxCategoryId = tax.Id, Name = "Original", Slug = Key(), Description = "Reusable description" };
        for (var i = 0; i < variantCount; i++)
        {
            var variant = new ProductVariant
            {
                ProductId = product.Id, Sku = Key(), Name = "Choice " + i, Price = new Money(12.50m + i, i == 0 ? "USD" : "EUR"),
                WeightGrams = 250 + i, Options = new Dictionary<string, string> { ["Size"] = "Medium" },
            };
            variant.Inventory = new InventoryItem(variant.Id, 15);
            variant.Inventory.Reserve(2);
            product.Variants.Add(variant);
        }
        product.Images.Add(new ProductImage { ProductId = product.Id, Url = "https://example.test/front.png", AltText = "Front", SortOrder = 3 });
        await factory.WithDbAsync(async db => { db.AddRange(category, tax, product); await db.SaveChangesAsync(); });
        return product;
    }

    [Fact]
    public async Task Clone_copies_catalog_values_but_resets_identity_stock_and_operational_memberships()
    {
        var source = await Source();
        await factory.WithDbAsync(async db =>
        {
            var customer = new Customer { Email = Key() + "@clone.test", FullName = "Buyer" };
            var review = new Review(source.Id, customer.Id, 5, "Title", "Reviewed source");
            review.Approve(DateTimeOffset.UtcNow);
            var tag = new Tag("Source tag", Key());
            db.Tags.Add(tag);
            var tagged = await db.Products.Include(p => p.Tags).SingleAsync(p => p.Id == source.Id);
            tagged.ReplaceTags([tag.Id]);
            db.ProductTags.AddRange(tagged.Tags);
            var collection = new ProductCollection("Source collection", Key());
            collection.Replace("Source collection", true, [source.Id]);
            var cart = new Cart { CustomerId = customer.Id };
            cart.AddItem(source.Variants[0].Id, 2);
            db.AddRange(customer, review, collection, cart);
            await db.SaveChangesAsync();
        });
        var admin = await Admin();
        var slug = Key();
        var mappings = source.Variants.Select(v => new { sourceVariantId = v.Id, sku = " " + Key() + " " }).ToArray();
        var response = await admin.PostAsJsonAsync($"/api/admin/products/{source.Id}/clone", new { name = " New draft ", slug, variantSkus = mappings });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<ClonedProductResponse>())!;
        Assert.NotEqual(source.Id, receipt.Id);
        Assert.False(receipt.IsActive);
        var cloneVariantIds = new List<Guid>();
        await factory.WithDbAsync(async db =>
        {
            var clone = await db.Products.Include(p => p.Variants).ThenInclude(v => v.Inventory).Include(p => p.Images).Include(p => p.Tags).SingleAsync(p => p.Id == receipt.Id);
            Assert.Equal("New draft", clone.Name);
            Assert.Equal(slug, clone.Slug);
            Assert.Equal(source.Description, clone.Description);
            Assert.Equal(source.CategoryId, clone.CategoryId);
            Assert.Equal(source.TaxCategoryId, clone.TaxCategoryId);
            Assert.False(clone.IsActive);
            Assert.Empty(clone.Tags);
            Assert.Equal(0, clone.TagVersion);
            Assert.Equal(source.Variants.Count, clone.Variants.Count);
            foreach (var original in source.Variants)
            {
                var sku = mappings.Single(m => m.sourceVariantId == original.Id).sku.Trim();
                var copied = clone.Variants.Single(v => v.Sku == sku);
                Assert.NotEqual(original.Id, copied.Id);
                Assert.Equal(original.Name, copied.Name);
                Assert.Equal(original.Price.Amount, copied.Price.Amount);
                Assert.Equal(original.Price.Currency, copied.Price.Currency);
                Assert.Equal(original.WeightGrams, copied.WeightGrams);
                Assert.Equal("Medium", copied.Options["Size"]);
                Assert.Equal(0, copied.Inventory!.QuantityOnHand);
                Assert.Equal(0, copied.Inventory.QuantityReserved);
                cloneVariantIds.Add(copied.Id);
            }
            var image = Assert.Single(clone.Images);
            Assert.NotEqual(source.Images[0].Id, image.Id);
            Assert.Equal(source.Images[0].Url, image.Url);
            Assert.Equal(source.Images[0].AltText, image.AltText);
            Assert.Equal(source.Images[0].SortOrder, image.SortOrder);
            Assert.False(await db.Reviews.AnyAsync(r => r.ProductId == clone.Id));
            Assert.False(await db.CollectionItems.AnyAsync(i => i.ProductId == clone.Id));
            Assert.False(await db.CartItems.AnyAsync(i => cloneVariantIds.Contains(i.ProductVariantId)));
            clone.Variants[0].Options["Size"] = "Large";
            await db.SaveChangesAsync();
        });
        await factory.WithDbAsync(async db =>
        {
            var original = await db.Products.Include(p => p.Variants).ThenInclude(v => v.Inventory).SingleAsync(p => p.Id == source.Id);
            Assert.True(original.IsActive);
            Assert.All(original.Variants, v => { Assert.Equal("Medium", v.Options["Size"]); Assert.Equal(15, v.Inventory!.QuantityOnHand); Assert.Equal(2, v.Inventory.QuantityReserved); });
            Assert.Equal(1, await db.Reviews.CountAsync(r => r.ProductId == source.Id));
            Assert.Equal(1, await db.CollectionItems.CountAsync(i => i.ProductId == source.Id));
        });
    }

    [Fact]
    public async Task Invalid_mappings_and_uniqueness_conflicts_leave_no_partial_draft()
    {
        var source = await Source();
        var admin = await Admin();
        var attempts = new[]
        {
            new[] { new CloneVariantSkuRequest(source.Variants[0].Id, Key()) },
            new[] { new CloneVariantSkuRequest(source.Variants[0].Id, Key()), new CloneVariantSkuRequest(Guid.NewGuid(), Key()) },
            new[] { new CloneVariantSkuRequest(source.Variants[0].Id, Key()), new CloneVariantSkuRequest(source.Variants[0].Id, Key()) },
            new[] { new CloneVariantSkuRequest(source.Variants[0].Id, "Same"), new CloneVariantSkuRequest(source.Variants[1].Id, "same") },
        };
        foreach (var mappings in attempts)
        {
            var slug = Key();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/admin/products/{source.Id}/clone", new { name = "Rejected", slug, variantSkus = mappings })).StatusCode);
            await factory.WithDbAsync(async db => Assert.False(await db.Products.AnyAsync(p => p.Slug == slug)));
        }
        var conflictingSlug = Key();
        var conflict = await admin.PostAsJsonAsync($"/api/admin/products/{source.Id}/clone", new
        {
            name = "No fragments", slug = conflictingSlug,
            variantSkus = source.Variants.Select(v => new CloneVariantSkuRequest(v.Id, v.Sku)).ToArray(),
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Products.AnyAsync(p => p.Slug == conflictingSlug));
            Assert.Equal(2, await db.ProductVariants.CountAsync(v => v.ProductId == source.Id));
            Assert.Equal(1, await db.ProductImages.CountAsync(i => i.ProductId == source.Id));
        });
    }

    [Fact]
    public async Task Losing_unique_sku_save_rolls_back_the_entire_draft_graph()
    {
        var source = await Source(1);
        var mappings = new Dictionary<Guid, string> { [source.Variants[0].Id] = Key() };
        // Both graphs can be prepared after observing the SKU as free; the database decides the winner.
        var winner = ProductDraftCloner.Clone(source, "Winner", Key(), mappings);
        var loser = ProductDraftCloner.Clone(source, "Loser", Key(), mappings);
        await factory.WithDbAsync(async db => { db.Products.Add(winner); await db.SaveChangesAsync(); });
        await factory.WithDbAsync(async db =>
        {
            db.Products.Add(loser);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(2067, Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(error.InnerException).SqliteExtendedErrorCode);
        });
        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Products.AnyAsync(p => p.Id == loser.Id));
            Assert.False(await db.ProductImages.AnyAsync(i => i.ProductId == loser.Id));
            Assert.False(await db.ProductVariants.AnyAsync(v => v.ProductId == loser.Id));
            var losingVariant = loser.Variants[0].Id;
            Assert.False(await db.InventoryItems.AnyAsync(i => i.ProductVariantId == losingVariant));
            Assert.True(await db.Products.AnyAsync(p => p.Id == winner.Id));
        });
    }

    [Fact]
    public async Task Empty_and_inactive_sources_are_supported_but_large_sources_and_nonadmins_are_rejected()
    {
        var admin = await Admin();
        var empty = await Source(0);
        await factory.WithDbAsync(async db => { (await db.Products.SingleAsync(p => p.Id == empty.Id)).IsActive = false; await db.SaveChangesAsync(); });
        var request = new { name = "Empty draft", slug = Key(), variantSkus = Array.Empty<CloneVariantSkuRequest>() };
        var cloned = await admin.PostAsJsonAsync($"/api/admin/products/{empty.Id}/clone", request);
        Assert.Equal(HttpStatusCode.Created, cloned.StatusCode);
        var receipt = (await cloned.Content.ReadFromJsonAsync<ClonedProductResponse>())!;
        await factory.WithDbAsync(async db => Assert.Empty(await db.ProductVariants.Where(v => v.ProductId == receipt.Id).ToListAsync()));
        var tooLarge = await Source(51);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/admin/products/{tooLarge.Id}/clone", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/admin/products/{Guid.NewGuid()}/clone", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync($"/api/admin/products/{empty.Id}/clone", request)).StatusCode);
        var customer = factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, Key() + "@clone.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync($"/api/admin/products/{empty.Id}/clone", request)).StatusCode);
    }
}
