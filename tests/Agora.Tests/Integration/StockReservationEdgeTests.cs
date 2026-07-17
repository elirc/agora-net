using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

/// <summary>
/// Edge cases around the reserve -> charge -> commit/release checkout pipeline:
/// stock changing between carting and checkout, partial multi-line failures,
/// and two carts competing for the same units.
/// </summary>
public class StockReservationEdgeTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Admin-only mutations are exercised throughout; authenticate up front.
    public Task InitializeAsync() => _client.AuthenticateAsAdminAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly AddressDto Address = new(
        "Edge Case", "9 Boundary Blvd", null, "Testville", "TS", "00000", "US");

    [Fact]
    public async Task Checkout_WhenStockDroppedAfterCarting_Returns409_AndReservesNothing()
    {
        // Cart 5 caps while stock allows it, then an admin stock take drops it to 3.
        var token = await CartWith("CAP-KHK", 5);
        (await _client.PutAsJsonAsync("/api/inventory/CAP-KHK", new SetStockRequest(3)))
            .EnsureSuccessStatusCode();

        var response = await Checkout(token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var stock = await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/CAP-KHK");
        Assert.Equal(3, stock!.QuantityOnHand);
        Assert.Equal(0, stock.QuantityReserved);
    }

    [Fact]
    public async Task Checkout_MultiLine_WhenOneLineShort_LeavesAllStockUntouched()
    {
        var token = await CartWith("TEE-BLK-S", 2);
        await AddTo(token, "KB-NIM-RED", 5); // stock 9
        (await _client.PutAsJsonAsync("/api/inventory/KB-NIM-RED", new SetStockRequest(2)))
            .EnsureSuccessStatusCode();

        var response = await Checkout(token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Neither line ends up reserved or deducted, including the line that had stock.
        var tees = await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/TEE-BLK-S");
        var keyboards = await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/KB-NIM-RED");
        Assert.Equal(0, tees!.QuantityReserved);
        Assert.Equal(40, tees.QuantityOnHand);
        Assert.Equal(0, keyboards!.QuantityReserved);
        Assert.Equal(2, keyboards.QuantityOnHand);
    }

    [Fact]
    public async Task TwoCarts_CompetingForLastUnits_SecondCheckoutFails()
    {
        (await _client.PutAsJsonAsync("/api/inventory/KET-EMB-1L", new SetStockRequest(10)))
            .EnsureSuccessStatusCode();
        var first = await CartWith("KET-EMB-1L", 6);
        var second = await CartWith("KET-EMB-1L", 6); // allowed: nothing reserved yet

        var firstResponse = await Checkout(first);
        var secondResponse = await Checkout(second);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var stock = await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/KET-EMB-1L");
        Assert.Equal(4, stock!.QuantityOnHand); // 10 - 6, loser reserved nothing
        Assert.Equal(0, stock.QuantityReserved);
    }

    [Fact]
    public async Task Checkout_ProductDeactivatedAfterCarting_Returns400()
    {
        var product = await _client.GetFromJsonAsync<ProductResponse>(
            "/api/products/by-slug/canvas-weekender-cap");
        var token = await CartWith("CAP-KHK", 1);

        var deactivate = await _client.PutAsJsonAsync($"/api/products/{product!.Id}",
            new UpdateProductRequest(product.CategoryId, product.Name, product.Slug, product.Description, false));
        deactivate.EnsureSuccessStatusCode();

        var response = await Checkout(token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Reactivate for any later tests in this class.
        await _client.PutAsJsonAsync($"/api/products/{product.Id}",
            new UpdateProductRequest(product.CategoryId, product.Name, product.Slug, product.Description, true));
    }

    [Fact]
    public async Task CancelledOrder_MakesUnitsSellableAgain()
    {
        (await _client.PutAsJsonAsync("/api/inventory/CDL-CDR-S", new SetStockRequest(2)))
            .EnsureSuccessStatusCode();

        var firstToken = await CartWith("CDL-CDR-S", 2);
        var firstCheckout = await Checkout(firstToken);
        Assert.Equal(HttpStatusCode.Created, firstCheckout.StatusCode);
        var order = await firstCheckout.Content.ReadFromJsonAsync<OrderResponse>();

        // Sold out now.
        var soldOutToken = await CreateCart();
        var variantId = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/CDL-CDR-S"))!
            .ProductVariantId;
        var soldOutAdd = await _client.PostAsJsonAsync($"/api/carts/{soldOutToken}/items",
            new AddCartItemRequest(variantId, 1));
        Assert.Equal(HttpStatusCode.Conflict, soldOutAdd.StatusCode);

        // Cancel restocks, so the same units can be sold again.
        (await _client.PostAsync($"/api/orders/{order!.Number}/cancel", null)).EnsureSuccessStatusCode();
        var retryToken = await CartWith("CDL-CDR-S", 2);
        var retryCheckout = await Checkout(retryToken);
        Assert.Equal(HttpStatusCode.Created, retryCheckout.StatusCode);
    }

    private async Task<string> CreateCart()
    {
        var response = await _client.PostAsync("/api/carts", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartResponse>())!.Token;
    }

    private async Task<string> CartWith(string sku, int quantity)
    {
        var token = await CreateCart();
        await AddTo(token, sku, quantity);
        return token;
    }

    private async Task AddTo(string token, string sku, int quantity)
    {
        var inventory = await _client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        var response = await _client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, quantity));
        response.EnsureSuccessStatusCode();
    }

    private Task<HttpResponseMessage> Checkout(string token) =>
        _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "edge@example.com", Address, null, "tok_visa"));
}
