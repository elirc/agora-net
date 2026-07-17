using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class ProductsApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Admin-only mutations are exercised throughout; authenticate up front.
    public Task InitializeAsync() => _client.AuthenticateAsAdminAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_ReturnsSeededCatalog()
    {
        var result = await _client.GetFromJsonAsync<PagedResult<ProductResponse>>("/api/products");

        Assert.NotNull(result);
        Assert.True(result.TotalCount >= 8);
        Assert.All(result.Items, p => Assert.NotEmpty(p.Variants));
    }

    [Fact]
    public async Task List_Paginates()
    {
        var result = await _client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            "/api/products?page=2&pageSize=3&sort=name");

        Assert.NotNull(result);
        Assert.Equal(2, result.Page);
        Assert.Equal(3, result.PageSize);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal((int)Math.Ceiling(result.TotalCount / 3.0), result.TotalPages);
    }

    [Fact]
    public async Task List_InvalidPageSize_Returns400()
    {
        var response = await _client.GetAsync("/api/products?pageSize=500");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_SearchByName_FindsProduct()
    {
        var result = await _client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            "/api/products?search=hoodie");

        Assert.NotNull(result);
        Assert.Contains(result.Items, p => p.Slug == "trailblazer-hoodie");
    }

    [Fact]
    public async Task List_SearchIsCaseInsensitive()
    {
        var result = await _client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            "/api/products?search=HOODIE");

        Assert.NotNull(result);
        Assert.Contains(result.Items, p => p.Slug == "trailblazer-hoodie");
    }

    [Fact]
    public async Task List_FilterByCategorySlug_ReturnsOnlyThatCategory()
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        var electronics = categories!.Single(c => c.Slug == "electronics");

        var result = await _client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            "/api/products?categorySlug=electronics");

        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, p => Assert.Equal(electronics.Id, p.CategoryId));
    }

    [Fact]
    public async Task List_FilterByMinPrice_ReturnsExpensiveProductsOnly()
    {
        var result = await _client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            "/api/products?minPrice=100");

        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, p => Assert.Contains(p.Variants, v => v.Price.Amount >= 100m));
    }

    [Fact]
    public async Task List_SortByPrice_CheapestFirst()
    {
        var result = await _client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            "/api/products?sort=price&pageSize=100");

        Assert.NotNull(result);
        var minPrices = result.Items.Select(p => p.Variants.Min(v => v.Price.Amount)).ToList();
        Assert.Equal(minPrices.OrderBy(x => x).ToList(), minPrices);
        Assert.Contains(result.Items, p => p.Slug == "cedar-scented-candle");
    }

    [Fact]
    public async Task List_SortByPriceDesc_MostExpensiveFirst()
    {
        var result = await _client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            "/api/products?sort=price_desc");

        Assert.NotNull(result);
        Assert.Equal("aurora-wireless-earbuds", result.Items[0].Slug); // 129.99, priciest seeded variant
    }

    [Fact]
    public async Task GetBySlug_ReturnsProductWithVariantsAndImages()
    {
        var product = await _client.GetFromJsonAsync<ProductResponse>(
            "/api/products/by-slug/classic-cotton-tee");

        Assert.NotNull(product);
        Assert.Equal(3, product.Variants.Count);
        Assert.NotEmpty(product.Images);
        Assert.Equal("M", product.Variants.Single(v => v.Sku == "TEE-BLK-M").Options["Size"]);
    }

    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        var response = await _client.GetAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_Returns201_WithVariants()
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        var home = categories!.Single(c => c.Slug == "home-kitchen");

        var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            home.Id,
            "Stoneware Mug Set",
            null,
            "Set of four 350ml mugs.",
            null,
            [new CreateVariantRequest("MUG-SET-4", "Set of 4", 32.00m, null, null)],
            [new CreateImageRequest("https://images.agora.example/mugs/main.jpg", "Mug set", 0)]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(created);
        Assert.Equal("stoneware-mug-set", created.Slug);
        Assert.True(created.IsActive);
        Assert.Single(created.Variants);
        Assert.Equal(32.00m, created.Variants[0].Price.Amount);
        Assert.Equal("USD", created.Variants[0].Price.Currency);
    }

    [Fact]
    public async Task Create_WithExistingSku_Returns409()
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");

        var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            categories![0].Id, "Sku Clash", "sku-clash", null, null,
            [new CreateVariantRequest("TEE-BLK-M", null, 10m, null, null)], null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithDuplicateSkusInRequest_Returns422()
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");

        var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            categories![0].Id, "Dup Sku", "dup-sku", null, null,
            [
                new CreateVariantRequest("DUP-1", null, 10m, null, null),
                new CreateVariantRequest("dup-1", null, 12m, null, null),
            ],
            null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithUnknownCategory_Returns422()
    {
        var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            Guid.NewGuid(), "No Category", "no-category", null, null,
            [new CreateVariantRequest("NOCAT-1", null, 10m, null, null)], null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNoVariants_Returns400()
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");

        var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            categories![0].Id, "Variantless", "variantless", null, null, [], null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesFields()
    {
        var created = await CreateSimpleProduct("Updatable Product", "UPD-1");

        var response = await _client.PutAsJsonAsync($"/api/products/{created.Id}",
            new UpdateProductRequest(created.CategoryId, "Updated Product", created.Slug, "new copy", false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal("Updated Product", updated!.Name);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task Delete_RemovesProduct()
    {
        var created = await CreateSimpleProduct("Deletable Product", "DEL-1");

        var deleteResponse = await _client.DeleteAsync($"/api/products/{created.Id}");
        var getResponse = await _client.GetAsync($"/api/products/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    private async Task<ProductResponse> CreateSimpleProduct(string name, string sku)
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            categories![0].Id, name, null, null, null,
            [new CreateVariantRequest(sku, null, 10m, null, null)], null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }
}
