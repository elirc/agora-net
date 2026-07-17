using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class SavedForLaterApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly AddressDto Address = new(
        "Save Later", "7 Someday St", null, "Deferville", "DF", "22222", "US");

    [Fact]
    public async Task SaveForLater_MovesLineOutOfTotals()
    {
        var (token, itemId) = await CartWithLine("TEE-BLK-S", 2); // 39.98

        var response = await _client.PostAsync($"/api/carts/{token}/items/{itemId}/save-for-later", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cart = await response.Content.ReadFromJsonAsync<CartResponse>();
        Assert.Empty(cart!.Items);
        var saved = Assert.Single(cart.SavedItems);
        Assert.Equal(2, saved.Quantity);
        Assert.Equal(0, cart.TotalQuantity);
        Assert.Equal(0m, cart.Subtotal.Amount);
    }

    [Fact]
    public async Task Activate_RestoresLineToTotals()
    {
        var (token, itemId) = await CartWithLine("CAP-KHK", 1); // 24.00
        await _client.PostAsync($"/api/carts/{token}/items/{itemId}/save-for-later", null);

        var response = await _client.PostAsync($"/api/carts/{token}/items/{itemId}/activate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cart = await response.Content.ReadFromJsonAsync<CartResponse>();
        Assert.Single(cart!.Items);
        Assert.Empty(cart.SavedItems);
        Assert.Equal(24.00m, cart.Subtotal.Amount);
    }

    [Fact]
    public async Task Checkout_SkipsSavedLines_AndKeepsThemAfterwards()
    {
        var (token, teeItemId) = await CartWithLine("TEE-BLK-S", 1); // 19.99 active
        await AddLine(token, "KET-EMB-1L", 1);                      // 39.99 -> save this one
        var cart = await _client.GetFromJsonAsync<CartResponse>($"/api/carts/{token}");
        var kettleItem = cart!.Items.Single(i => i.Sku == "KET-EMB-1L");
        await _client.PostAsync($"/api/carts/{token}/items/{kettleItem.Id}/save-for-later", null);

        var checkout = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "later@example.com", Address, null, "tok_visa"));

        Assert.Equal(HttpStatusCode.Created, checkout.StatusCode);
        var order = await checkout.Content.ReadFromJsonAsync<OrderResponse>();
        var line = Assert.Single(order!.Items);
        Assert.Equal("TEE-BLK-S", line.Sku);
        Assert.Equal(19.99m, order.Subtotal);

        // Saved line survives checkout.
        var after = await _client.GetFromJsonAsync<CartResponse>($"/api/carts/{token}");
        Assert.Empty(after!.Items);
        var saved = Assert.Single(after.SavedItems);
        Assert.Equal("KET-EMB-1L", saved.Sku);
        _ = teeItemId;
    }

    [Fact]
    public async Task Checkout_OnlySavedLines_Returns400()
    {
        var (token, itemId) = await CartWithLine("CAP-KHK", 1);
        await _client.PostAsync($"/api/carts/{token}/items/{itemId}/save-for-later", null);

        var checkout = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "later@example.com", Address, null, "tok_visa"));

        Assert.Equal(HttpStatusCode.BadRequest, checkout.StatusCode);
    }

    [Fact]
    public async Task AddingSavedVariantAgain_ReactivatesLine()
    {
        var (token, itemId) = await CartWithLine("TEE-BLK-S", 2);
        await _client.PostAsync($"/api/carts/{token}/items/{itemId}/save-for-later", null);

        await AddLine(token, "TEE-BLK-S", 1);

        var cart = await _client.GetFromJsonAsync<CartResponse>($"/api/carts/{token}");
        var line = Assert.Single(cart!.Items);
        Assert.Equal(3, line.Quantity); // merged 2 + 1
        Assert.Empty(cart.SavedItems);
    }

    [Fact]
    public async Task SaveForLater_UnknownItem_Returns404()
    {
        var (token, _) = await CartWithLine("CAP-KHK", 1);

        var response = await _client.PostAsync(
            $"/api/carts/{token}/items/{Guid.NewGuid()}/save-for-later", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(string Token, Guid ItemId)> CartWithLine(string sku, int quantity)
    {
        var cartResponse = await _client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;
        var cart = await AddLine(token, sku, quantity);
        return (token, cart.Items.Single(i => i.Sku == sku).Id);
    }

    private async Task<CartResponse> AddLine(string token, string sku, int quantity)
    {
        var inventory = await _client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        var response = await _client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, quantity));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartResponse>())!;
    }
}
