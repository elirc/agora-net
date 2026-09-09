using System.Net;
using System.Net.Http.Json;
using System.Text;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class CatalogEditingApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private static string Key() => Guid.NewGuid().ToString("N");
    private readonly HttpClient _client = factory.CreateClient();
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private async Task<HttpClient> Admin()
    {
        var client = factory.CreateClient();
        await client.AuthenticateAsAdminAsync();
        return client;
    }
    private async Task<Product> Product(int images = 0)
    {
        var category = new Category { Name = Key(), Slug = Key() };
        var product = new Product { CategoryId = category.Id, Name = "Original product", Slug = Key() };
        var variant = new ProductVariant { ProductId = product.Id, Sku = Key(), Name = "Original choice", Price = new Money(10), WeightGrams = 100, Options = new() { ["Size"] = "M" } };
        variant.Inventory = new InventoryItem(variant.Id, 20);
        product.Variants.Add(variant);
        for (var i = 0; i < images; i++) product.Images.Add(new ProductImage { ProductId = product.Id, Url = $"https://example.test/{i}.png", SortOrder = i * 2 });
        await factory.WithDbAsync(async db => { db.AddRange(category, product); await db.SaveChangesAsync(); });
        return product;
    }
    private async Task<string> CartWith(Guid variantId)
    {
        var cart = await Read<CartResponse>(await _client.PostAsync("/api/carts", null));
        (await _client.PostAsJsonAsync($"/api/carts/{cart.Token}/items", new AddCartItemRequest(variantId, 1))).EnsureSuccessStatusCode();
        return cart.Token;
    }

    [Fact]
    public async Task Variant_edit_changes_live_carts_but_preserves_purchase_snapshots_and_rejects_stale_edits()
    {
        var product = await Product();
        var variant = product.Variants[0];
        var purchasedCart = await CartWith(variant.Id);
        var address = new AddressDto("Buyer", "1 Test Street", null, "London", "LDN", "EC1A 1AA", "GB");
        var order = await Read<OrderResponse>(await _client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(purchasedCart, Key() + "@edit.test", address, null, "tok_visa")));
        var liveCart = await CartWith(variant.Id);
        var admin = await Admin();
        var feedBefore = await admin.GetFromJsonAsync<CatalogBootstrapResult>("/api/admin/catalog-sync/bootstrap");
        var path = $"/api/admin/products/{product.Id}/variants/{variant.Id}";
        var original = (await admin.GetFromJsonAsync<AdminVariantResponse>(path))!;
        Assert.Equal(0, original.Version);
        var replacement = new EditVariantRequest(" Updated choice ", 24.50m, 750, new() { [" Size "] = " Large " }, 0);
        var edited = await Read<AdminVariantResponse>(await admin.PutAsJsonAsync(path, replacement));
        Assert.Equal(1, edited.Version);
        Assert.Equal("Updated choice", edited.Name);
        Assert.Equal(24.50m, edited.Price.Amount);
        Assert.Equal("USD", edited.Price.Currency);
        Assert.Equal(variant.Sku, edited.Sku);
        Assert.Equal(750, edited.WeightGrams);
        Assert.Equal("Large", edited.Options["Size"]);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path, replacement with { Name = "Stale overwrite" })).StatusCode);
        var cart = (await _client.GetFromJsonAsync<CartResponse>($"/api/carts/{liveCart}"))!;
        var line = Assert.Single(cart.Items);
        Assert.Equal(24.50m, line.UnitPrice.Amount);
        Assert.Equal("Updated choice", line.VariantName);
        await factory.WithDbAsync(async db =>
        {
            var savedOrder = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Number == order.Number);
            var bought = Assert.Single(savedOrder.Items);
            Assert.Equal(10m, bought.UnitPrice);
            Assert.Equal("Original choice", bought.VariantName);
            Assert.Equal(1, (await db.ProductVariants.SingleAsync(v => v.Id == variant.Id)).Version);
        });
        var feedAfter = await admin.GetFromJsonAsync<CatalogChangesResult>(
            $"/api/admin/catalog-sync/changes?after={feedBefore!.Watermark}&limit=100");
        var variantEvent = Assert.Single(feedAfter!.Changes);
        Assert.Equal(product.Id, variantEvent.ProductId);
        Assert.Equal("Updated choice", variantEvent.Product!.Variants.Single().Name);
    }

    [Fact]
    public async Task Variant_validation_keeps_the_entire_old_value_and_rejects_duplicate_json_keys()
    {
        var product = await Product();
        var variant = product.Variants[0];
        var admin = await Admin();
        var path = $"/api/admin/products/{product.Id}/variants/{variant.Id}";
        var valid = new EditVariantRequest("Updated", 12m, 200, new() { ["Size"] = "L" }, 0);
        foreach (var invalid in new[]
        {
            valid with { Price = 1.001m }, valid with { Price = -1 }, valid with { Price = 1_000_001 },
            valid with { WeightGrams = -1 }, valid with { WeightGrams = 1_000_001 }, valid with { Name = new string('x', 121) },
            valid with { Options = new() { ["Size"] = "M", [" size "] = "L" } },
            valid with { Options = new() { [" "] = "M" } }, valid with { Options = new() { ["Size"] = " " } },
            valid with { Options = Enumerable.Range(0, 21).ToDictionary(i => "Key" + i, _ => "Value") },
            valid with { ExpectedVersion = null },
        }) Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path, invalid)).StatusCode);
        using var duplicateJson = new StringContent("""{"name":"Changed","price":11,"weightGrams":100,"options":{"Size":"M","Size":"L"},"expectedVersion":0}""", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync(path, duplicateJson)).StatusCode);
        var after = (await admin.GetFromJsonAsync<AdminVariantResponse>(path))!;
        Assert.Equal("Original choice", after.Name);
        Assert.Equal(10m, after.Price.Amount);
        Assert.Equal(100, after.WeightGrams);
        Assert.Equal("M", after.Options["Size"]);
        Assert.Equal(0, after.Version);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/admin/products/{Guid.NewGuid()}/variants/{variant.Id}", valid)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(path)).StatusCode);
        var customer = factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, Key() + "@edit.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PutAsJsonAsync(path, valid)).StatusCode);
        var boundary = await Read<AdminVariantResponse>(await admin.PutAsJsonAsync(path, valid with
        {
            Price = 1_000_000, WeightGrams = 1_000_000, Name = " " + new string('n', 120) + " ", Options = new(),
        }));
        Assert.Equal(1_000_000m, boundary.Price.Amount);
        Assert.Empty(boundary.Options);
    }

    [Fact]
    public async Task Gallery_add_reorder_remove_updates_public_primary_image_and_rejects_stale_or_partial_orders()
    {
        var product = await Product(3);
        var admin = await Admin();
        var feedBefore = await admin.GetFromJsonAsync<CatalogBootstrapResult>("/api/admin/catalog-sync/bootstrap");
        var path = $"/api/admin/products/{product.Id}/images";
        var added = await Read<GalleryResponse>(await admin.PostAsJsonAsync(path, new AddGalleryImageRequest("https://example.test/new.png", "New view", 0)));
        Assert.Equal(1, added.Version);
        Assert.Equal(new[] { 0, 1, 2, 3 }, added.Images.Select(i => i.SortOrder));
        var reversed = added.Images.Reverse().Select(i => i.Id).ToList();
        var reordered = await Read<GalleryResponse>(await admin.PutAsJsonAsync(path + "/order", new ReorderGalleryRequest(reversed, 1)));
        Assert.Equal(reversed, reordered.Images.Select(i => i.Id));
        Assert.Equal(2, reordered.Version);
        Assert.Equal(reversed[0], (await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}"))!.PrimaryImage!.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path + "/order", new ReorderGalleryRequest(reversed, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync(path + "/order", new ReorderGalleryRequest(reversed.Take(3).ToList(), 2))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync(path + "/order", new ReorderGalleryRequest([reversed[0], reversed[0], reversed[2], reversed[3]], 2))).StatusCode);
        Assert.Equal(reversed, (await admin.GetFromJsonAsync<GalleryResponse>(path))!.Images.Select(i => i.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"{path}/{reversed[0]}?expectedVersion=1")).StatusCode);
        var removed = await Read<GalleryResponse>(await admin.DeleteAsync($"{path}/{reversed[0]}?expectedVersion=2"));
        Assert.Equal(3, removed.Version);
        Assert.Equal(new[] { 0, 1, 2 }, removed.Images.Select(i => i.SortOrder));
        Assert.Equal(reversed[1], (await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}"))!.PrimaryImage!.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"{path}/{reversed[1]}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{path}/{Guid.NewGuid()}?expectedVersion=3")).StatusCode);
        var feedAfter = await admin.GetFromJsonAsync<CatalogChangesResult>(
            $"/api/admin/catalog-sync/changes?after={feedBefore!.Watermark}&limit=100");
        Assert.Equal(3, feedAfter!.Changes.Count);
        Assert.All(feedAfter.Changes, change => Assert.Equal(product.Id, change.ProductId));
    }

    [Fact]
    public async Task Empty_gallery_and_invalid_links_have_explicit_behavior()
    {
        var product = await Product();
        var admin = await Admin();
        var path = $"/api/admin/products/{product.Id}/images";
        var empty = await Read<GalleryResponse>(await admin.PutAsJsonAsync(path + "/order", new ReorderGalleryRequest([], 0)));
        Assert.Empty(empty.Images);
        Assert.Equal(1, empty.Version);
        foreach (var url in new[] { "relative.png", "ftp://example.test/a", "javascript:alert(1)" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path, new AddGalleryImageRequest(url, null, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path, new AddGalleryImageRequest("https://example.test/a", new string('x', 501), 1))).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<GalleryResponse>(path))!.Images);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync(path, new AddGalleryImageRequest("https://example.test/a", null, 1))).StatusCode);
        var customer = factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, Key() + "@gallery.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Legacy_large_galleries_can_be_cloned_reordered_and_reduced_but_not_extended()
    {
        var product = await Product(11);
        var admin = await Admin();
        var path = $"/api/admin/products/{product.Id}/images";
        var before = (await admin.GetFromJsonAsync<GalleryResponse>(path))!;
        Assert.Equal(11, before.Images.Count);
        var reversed = before.Images.Reverse().Select(i => i.Id).ToList();
        var reordered = await Read<GalleryResponse>(await admin.PutAsJsonAsync(path + "/order", new ReorderGalleryRequest(reversed, 0)));
        Assert.Equal(11, reordered.Images.Count);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync(path, new AddGalleryImageRequest("https://example.test/extra", null, 1))).StatusCode);
        var feedBefore = await admin.GetFromJsonAsync<CatalogBootstrapResult>("/api/admin/catalog-sync/bootstrap");
        var clone = await Read<ClonedProductResponse>(await admin.PostAsJsonAsync($"/api/admin/products/{product.Id}/clone",
            new CloneProductRequest("Legacy copy", Key(), [new CloneVariantSkuRequest(product.Variants[0].Id, Key())])));
        Assert.Equal(11, (await admin.GetFromJsonAsync<GalleryResponse>($"/api/admin/products/{clone.Id}/images"))!.Images.Count);
        var cloneChanges = await admin.GetFromJsonAsync<CatalogChangesResult>(
            $"/api/admin/catalog-sync/changes?after={feedBefore!.Watermark}&limit=100");
        Assert.Contains(cloneChanges!.Changes, change => change.ProductId == clone.Id && change.Kind == "Upsert");
        await Read<GalleryResponse>(await admin.DeleteAsync($"{path}/{reversed[0]}?expectedVersion=1"));
        await Read<GalleryResponse>(await admin.DeleteAsync($"{path}/{reversed[1]}?expectedVersion=2"));
        var extended = await Read<GalleryResponse>(await admin.PostAsJsonAsync(path, new AddGalleryImageRequest("https://example.test/new", null, 3)));
        Assert.Equal(10, extended.Images.Count);
        var newRequest = new CreateProductRequest(product.CategoryId, "Too many initial images", Key(), null, true,
            [new CreateVariantRequest(Key(), "Choice", 10, "USD", null)],
            Enumerable.Range(0, 11).Select(i => new CreateImageRequest($"https://example.test/{i}", null, i)).ToList());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/products", newRequest)).StatusCode);
    }
}
