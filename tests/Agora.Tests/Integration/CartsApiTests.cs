using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class CartsApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_ReturnsEmptyCartWithToken()
    {
        var response = await _client.PostAsync("/api/carts", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cart = await response.Content.ReadFromJsonAsync<CartResponse>();
        Assert.NotNull(cart);
        Assert.NotEmpty(cart.Token);
        Assert.Empty(cart.Items);
        Assert.Equal(0m, cart.Subtotal.Amount);
    }

    [Fact]
    public async Task GetByToken_UnknownToken_Returns404()
    {
        var response = await _client.GetAsync("/api/carts/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddItem_AddsLineWithPricing()
    {
        var cart = await CreateCart();
        var variantId = await GetVariantId("classic-cotton-tee", "TEE-BLK-M");

        var response = await _client.PostAsJsonAsync($"/api/carts/{cart.Token}/items",
            new AddCartItemRequest(variantId, 2));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CartResponse>();
        var line = Assert.Single(updated!.Items);
        Assert.Equal("TEE-BLK-M", line.Sku);
        Assert.Equal("Classic Cotton Tee", line.ProductName);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(19.99m, line.UnitPrice.Amount);
        Assert.Equal(39.98m, line.LineTotal.Amount);
        Assert.Equal(39.98m, updated.Subtotal.Amount);
    }

    [Fact]
    public async Task AddItem_SameVariantTwice_MergesLines()
    {
        var cart = await CreateCart();
        var variantId = await GetVariantId("classic-cotton-tee", "TEE-WHT-M");

        await _client.PostAsJsonAsync($"/api/carts/{cart.Token}/items", new AddCartItemRequest(variantId, 2));
        var response = await _client.PostAsJsonAsync($"/api/carts/{cart.Token}/items",
            new AddCartItemRequest(variantId, 3));

        var updated = await response.Content.ReadFromJsonAsync<CartResponse>();
        var line = Assert.Single(updated!.Items);
        Assert.Equal(5, line.Quantity);
    }

    [Fact]
    public async Task AddItem_UnknownVariant_Returns422()
    {
        var cart = await CreateCart();

        var response = await _client.PostAsJsonAsync($"/api/carts/{cart.Token}/items",
            new AddCartItemRequest(Guid.NewGuid(), 1));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AddItem_ZeroQuantity_Returns400()
    {
        var cart = await CreateCart();
        var variantId = await GetVariantId("classic-cotton-tee", "TEE-BLK-S");

        var response = await _client.PostAsJsonAsync($"/api/carts/{cart.Token}/items",
            new AddCartItemRequest(variantId, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddItem_ExceedingAvailableStock_Returns409()
    {
        var cart = await CreateCart();
        var variantId = await GetVariantId("nimbus-mechanical-keyboard", "KB-NIM-RED"); // 9 in stock

        var response = await _client.PostAsJsonAsync($"/api/carts/{cart.Token}/items",
            new AddCartItemRequest(variantId, 10));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AddItem_OutOfStockVariant_Returns409()
    {
        var cart = await CreateCart();
        var variantId = await GetVariantId("cedar-scented-candle", "CDL-CDR-L"); // 0 in stock

        var response = await _client.PostAsJsonAsync($"/api/carts/{cart.Token}/items",
            new AddCartItemRequest(variantId, 1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItem_ChangesQuantity()
    {
        var cart = await CreateCart();
        var variantId = await GetVariantId("trailblazer-hoodie", "HOOD-GRY-M");
        var added = await AddItem(cart.Token, variantId, 1);

        var response = await _client.PutAsJsonAsync(
            $"/api/carts/{cart.Token}/items/{added.Items[0].Id}", new UpdateCartItemRequest(4));

        var updated = await response.Content.ReadFromJsonAsync<CartResponse>();
        Assert.Equal(4, updated!.Items[0].Quantity);
        Assert.Equal(4 * 54.50m, updated.Subtotal.Amount);
    }

    [Fact]
    public async Task UpdateItem_ToZero_RemovesLine()
    {
        var cart = await CreateCart();
        var variantId = await GetVariantId("trailblazer-hoodie", "HOOD-GRY-L");
        var added = await AddItem(cart.Token, variantId, 2);

        var response = await _client.PutAsJsonAsync(
            $"/api/carts/{cart.Token}/items/{added.Items[0].Id}", new UpdateCartItemRequest(0));

        var updated = await response.Content.ReadFromJsonAsync<CartResponse>();
        Assert.Empty(updated!.Items);
    }

    [Fact]
    public async Task UpdateItem_UnknownItem_Returns404()
    {
        var cart = await CreateCart();

        var response = await _client.PutAsJsonAsync(
            $"/api/carts/{cart.Token}/items/{Guid.NewGuid()}", new UpdateCartItemRequest(1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveItem_DeletesLine()
    {
        var cart = await CreateCart();
        var variantId = await GetVariantId("canvas-weekender-cap", "CAP-KHK");
        var added = await AddItem(cart.Token, variantId, 1);

        var response = await _client.DeleteAsync($"/api/carts/{cart.Token}/items/{added.Items[0].Id}");

        var updated = await response.Content.ReadFromJsonAsync<CartResponse>();
        Assert.Empty(updated!.Items);
    }

    [Fact]
    public async Task Clear_EmptiesCart()
    {
        var cart = await CreateCart();
        var variantId = await GetVariantId("volt-65w-gan-charger", "CHG-65W");
        await AddItem(cart.Token, variantId, 2);

        var clearResponse = await _client.DeleteAsync($"/api/carts/{cart.Token}");
        var fetched = await _client.GetFromJsonAsync<CartResponse>($"/api/carts/{cart.Token}");

        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);
        Assert.Empty(fetched!.Items);
    }

    private async Task<CartResponse> CreateCart()
    {
        var response = await _client.PostAsync("/api/carts", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartResponse>())!;
    }

    private async Task<CartResponse> AddItem(string token, Guid variantId, int quantity)
    {
        var response = await _client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(variantId, quantity));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartResponse>())!;
    }

    private async Task<Guid> GetVariantId(string productSlug, string sku)
    {
        var product = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/by-slug/{productSlug}");
        return product!.Variants.Single(v => v.Sku == sku).Id;
    }
}
