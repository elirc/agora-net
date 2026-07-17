using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class OrdersApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly AddressDto Address = new(
        "Grace Hopper", "2 Compiler Court", "Suite 9", "Arlington", "VA", "22201", "US");

    [Fact]
    public async Task GetByNumber_ReturnsOrder()
    {
        var placed = await PlaceOrder("TEE-BLK-S", 1);

        var fetched = await _client.GetFromJsonAsync<OrderResponse>($"/api/orders/{placed.Number}");

        Assert.NotNull(fetched);
        Assert.Equal(placed.Number, fetched.Number);
        Assert.Equal("Paid", fetched.Status);
        Assert.Equal("Grace Hopper", fetched.ShippingAddress.FullName);
    }

    [Fact]
    public async Task GetByNumber_Unknown_Returns404()
    {
        var response = await _client.GetAsync("/api/orders/ORD-00000000-XXXX");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Fulfill_PaidOrder_Succeeds()
    {
        var placed = await PlaceOrder("TEE-BLK-M", 1);

        var response = await _client.PostAsync($"/api/orders/{placed.Number}/fulfill", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal("Fulfilled", order!.Status);
        Assert.NotNull(order.FulfilledAt);
    }

    [Fact]
    public async Task Fulfill_Twice_Returns409()
    {
        var placed = await PlaceOrder("TEE-WHT-M", 1);
        await _client.PostAsync($"/api/orders/{placed.Number}/fulfill", null);

        var response = await _client.PostAsync($"/api/orders/{placed.Number}/fulfill", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_PaidOrder_RestoresStock()
    {
        var before = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/HOOD-GRY-L"))!;
        var placed = await PlaceOrder("HOOD-GRY-L", 2);

        var response = await _client.PostAsync($"/api/orders/{placed.Number}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal("Cancelled", order!.Status);

        var after = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/HOOD-GRY-L"))!;
        Assert.Equal(before.QuantityOnHand, after.QuantityOnHand);
        Assert.Equal(0, after.QuantityReserved);
    }

    [Fact]
    public async Task Refund_FulfilledOrder_RestoresStock()
    {
        var before = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/KET-EMB-1L"))!;
        var placed = await PlaceOrder("KET-EMB-1L", 3);
        await _client.PostAsync($"/api/orders/{placed.Number}/fulfill", null);

        var response = await _client.PostAsync($"/api/orders/{placed.Number}/refund", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal("Refunded", order!.Status);
        Assert.NotNull(order.RefundedAt);

        var after = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/KET-EMB-1L"))!;
        Assert.Equal(before.QuantityOnHand, after.QuantityOnHand);
    }

    [Fact]
    public async Task Refund_CancelledOrder_Returns409()
    {
        var placed = await PlaceOrder("CDL-CDR-S", 1);
        await _client.PostAsync($"/api/orders/{placed.Number}/cancel", null);

        var response = await _client.PostAsync($"/api/orders/{placed.Number}/refund", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_UnknownOrder_Returns404()
    {
        var response = await _client.PostAsync("/api/orders/ORD-NOPE/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<OrderResponse> PlaceOrder(string sku, int quantity)
    {
        var cartResponse = await _client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var inventory = await _client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        var addResponse = await _client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, quantity));
        addResponse.EnsureSuccessStatusCode();

        var checkoutResponse = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "grace@example.com", Address, null, "tok_visa"));
        checkoutResponse.EnsureSuccessStatusCode();
        return (await checkoutResponse.Content.ReadFromJsonAsync<OrderResponse>())!;
    }
}
