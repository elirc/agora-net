using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

/// <summary>
/// Boundary and validation edges: pagination at and just past the cap, cart
/// quantities at their limits, empty search results, and malformed routes.
/// </summary>
public class BoundaryValidationTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Products_PageSizeExactly100_Succeeds()
    {
        var response = await _client.GetAsync("/api/products?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>();
        Assert.Equal(100, page!.PageSize);
    }

    [Theory]
    [InlineData("/api/products?page=0")]
    [InlineData("/api/products?page=-1")]
    [InlineData("/api/products?pageSize=0")]
    [InlineData("/api/products?pageSize=-5")]
    [InlineData("/api/products?pageSize=101")]
    public async Task Products_PaginationOutOfRange_Returns400(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Products_PageBeyondLastPage_ReturnsEmptyItems_NotAnError()
    {
        var response = await _client.GetAsync("/api/products?page=999&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>();
        Assert.Empty(page!.Items);
        Assert.True(page.TotalCount > 0);
    }

    [Fact]
    public async Task Search_NoMatches_ReturnsEmptyPageWithZeroTotal()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            "/api/products?search=xyzzy-plugh-nothing");

        Assert.Empty(page!.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public async Task CartQuantity_AtMax99_Succeeds_MergingPastItFails()
    {
        var token = await CreateCart();
        var variantId = (await _client.GetFromJsonAsync<InventoryResponse>(
            "/api/inventory/CDL-CDR-S"))!.ProductVariantId; // seeded stock 100

        var atMax = await _client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(variantId, 99));
        Assert.Equal(HttpStatusCode.OK, atMax.StatusCode);

        // A merge that would land on 100 breaches the per-line cap of 99.
        var pastMax = await _client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(variantId, 1));
        Assert.Equal(HttpStatusCode.BadRequest, pastMax.StatusCode);

        var cart = await _client.GetFromJsonAsync<CartResponse>($"/api/carts/{token}");
        Assert.Equal(99, cart!.Items.Single().Quantity); // failed merge changed nothing
    }

    [Fact]
    public async Task CartUpdate_To99Succeeds_100FailsValidation()
    {
        var token = await CreateCart();
        var variantId = (await _client.GetFromJsonAsync<InventoryResponse>(
            "/api/inventory/CDL-CDR-S"))!.ProductVariantId;
        var add = await _client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(variantId, 1));
        var itemId = (await add.Content.ReadFromJsonAsync<CartResponse>())!.Items.Single().Id;

        var to99 = await _client.PutAsJsonAsync($"/api/carts/{token}/items/{itemId}",
            new UpdateCartItemRequest(99));
        Assert.Equal(HttpStatusCode.OK, to99.StatusCode);

        var to100 = await _client.PutAsJsonAsync($"/api/carts/{token}/items/{itemId}",
            new UpdateCartItemRequest(100));
        Assert.Equal(HttpStatusCode.BadRequest, to100.StatusCode);
    }

    [Fact]
    public async Task ProductRoute_MalformedGuid_Returns404()
    {
        var response = await _client.GetAsync("/api/products/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GiftCardBalance_UnknownCode_Returns404()
    {
        var response = await _client.GetAsync("/api/gift-cards/GC-DOES-NOT-EXIST");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<string> CreateCart()
    {
        var response = await _client.PostAsync("/api/carts", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartResponse>())!.Token;
    }
}
