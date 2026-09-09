using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public sealed class CatalogSyncApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>, IAsyncLifetime
{
    private readonly HttpClient admin = factory.CreateClient();
    public Task InitializeAsync() => admin.AuthenticateAsAdminAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Bootstrap_then_upsert_and_delete_maintain_a_local_mirror()
    {
        var bootstrap = await admin.GetFromJsonAsync<CatalogBootstrapResult>(
            "/api/admin/catalog-sync/bootstrap");
        Assert.NotNull(bootstrap);
        var original = bootstrap.Products.First();
        var mirror = bootstrap.Products.ToDictionary(product => product.Id);

        var update = await admin.PutAsJsonAsync($"/api/products/{original.Id}",
            new UpdateProductRequest(original.CategoryId, original.Name + " revised", original.Slug,
                original.Description, original.IsActive));
        update.EnsureSuccessStatusCode();
        var delete = await admin.DeleteAsync($"/api/products/{bootstrap.Products[1].Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var page = await admin.GetFromJsonAsync<CatalogChangesResult>(
            $"/api/admin/catalog-sync/changes?after={bootstrap.Watermark}&limit=100");
        Assert.Equal(2, page!.Changes.Count);
        Assert.Equal(new[] { bootstrap.Watermark + 1, bootstrap.Watermark + 2 }, page.Changes.Select(change => change.Sequence));
        foreach (var change in page.Changes)
        {
            if (change.Kind == "Delete") mirror.Remove(change.ProductId);
            else mirror[change.ProductId] = change.Product!;
        }
        Assert.Equal(original.Name + " revised", mirror[original.Id].Name);
        Assert.DoesNotContain(bootstrap.Products[1].Id, mirror.Keys);
        Assert.Null(page.Changes[1].Product);

        var replay = await admin.GetFromJsonAsync<CatalogChangesResult>(
            $"/api/admin/catalog-sync/changes?after={bootstrap.Watermark}&limit=100");
        Assert.Equal(page.Changes.Select(x => x.Sequence), replay!.Changes.Select(x => x.Sequence));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/admin/catalog-sync/changes?after=999999&limit=10")).StatusCode);
    }

    [Fact]
    public async Task Inventory_only_write_does_not_publish_catalog_change()
    {
        var baseline = await admin.GetFromJsonAsync<CatalogBootstrapResult>(
            "/api/admin/catalog-sync/bootstrap");
        var stock = await admin.GetFromJsonAsync<InventoryResponse>("/api/inventory/TEE-BLK-M");
        (await admin.PutAsJsonAsync("/api/inventory/TEE-BLK-M",
            new SetStockRequest(stock!.QuantityOnHand + 1))).EnsureSuccessStatusCode();
        var page = await admin.GetFromJsonAsync<CatalogChangesResult>(
            $"/api/admin/catalog-sync/changes?after={baseline!.Watermark}&limit=100");
        Assert.Empty(page!.Changes);
        Assert.Equal(baseline.Watermark, page.HighWatermark);
    }

    [Fact]
    public async Task Product_create_publishes_one_complete_upsert_snapshot()
    {
        var baseline = await admin.GetFromJsonAsync<CatalogBootstrapResult>(
            "/api/admin/catalog-sync/bootstrap");
        var categoryId = baseline!.Products.First().CategoryId;
        var key = Guid.NewGuid().ToString("N");
        var request = new CreateProductRequest(categoryId, "Feed-created product", $"feed-created-{key}",
            "Created through the public writer", true,
            [new CreateVariantRequest($"FEED-{key}", "Choice", 19.95m, "USD", null, 120)], []);

        var createdResponse = await admin.PostAsJsonAsync("/api/products", request);
        createdResponse.EnsureSuccessStatusCode();
        var created = await createdResponse.Content.ReadFromJsonAsync<ProductResponse>();
        var page = await admin.GetFromJsonAsync<CatalogChangesResult>(
            $"/api/admin/catalog-sync/changes?after={baseline.Watermark}&limit=100");

        var change = Assert.Single(page!.Changes);
        Assert.Equal(created!.Id, change.ProductId);
        Assert.Equal("Upsert", change.Kind);
        Assert.Equal(created.Name, change.Product!.Name);
        Assert.Equal(created.Variants.Single().Sku, change.Product.Variants.Single().Sku);
    }

    [Fact]
    public async Task Endpoints_are_admin_only()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/admin/catalog-sync/bootstrap")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/admin/catalog-sync/changes?after=0")).StatusCode);
    }
}
