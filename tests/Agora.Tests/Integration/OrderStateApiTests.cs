using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

/// <summary>
/// Invalid order/RMA transitions and purchase-gated reviews through the HTTP
/// surface: every illegal move must surface as a ProblemDetails 409/422, and
/// legitimate purchasers must not lose their review right mid-fulfillment.
/// </summary>
public class OrderStateApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private const string GuestEmail = "states@example.com";

    private static readonly AddressDto Address = new(
        "Stately Home", "4 Machine Way", null, "Automaton", "AU", "44444", "US");

    [Fact]
    public async Task Cancel_FulfilledOrder_Returns409()
    {
        var order = await PlaceOrder("CAP-KHK", 1);
        await Fulfill(order.Number);

        var response = await (await AdminClient()).PostAsync($"/api/orders/{order.Number}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Invalid order state", problem.RootElement.GetProperty("title").GetString());
        Assert.Contains("Fulfilled", problem.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Refund_Twice_Returns409()
    {
        var order = await PlaceOrder("CAP-KHK", 1);
        (await (await AdminClient()).PostAsync($"/api/orders/{order.Number}/refund", null))
            .EnsureSuccessStatusCode();

        var second = await (await AdminClient()).PostAsync($"/api/orders/{order.Number}/refund", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Cancel_RefundedOrder_Returns409()
    {
        var order = await PlaceOrder("CAP-KHK", 1);
        (await (await AdminClient()).PostAsync($"/api/orders/{order.Number}/refund", null))
            .EnsureSuccessStatusCode();

        var response = await (await AdminClient()).PostAsync($"/api/orders/{order.Number}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Fulfill_RefundedOrder_Returns409()
    {
        var order = await PlaceOrder("CAP-KHK", 1);
        (await (await AdminClient()).PostAsync($"/api/orders/{order.Number}/refund", null))
            .EnsureSuccessStatusCode();

        var admin = await AdminClient();
        var response = await admin.PostAsync($"/api/orders/{order.Number}/fulfill", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Rma_OnCancelledOrder_Returns409()
    {
        var order = await PlaceOrder("CAP-KHK", 1);
        (await (await AdminClient()).PostAsync($"/api/orders/{order.Number}/cancel", null))
            .EnsureSuccessStatusCode();

        var response = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "Damaged", null,
                [new ReturnLineDto(order.Items.Single().Id, 1)]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Rma_OnRefundedOrder_Returns409()
    {
        var order = await PlaceOrder("CAP-KHK", 1);
        await Fulfill(order.Number);
        (await (await AdminClient()).PostAsync($"/api/orders/{order.Number}/refund", null))
            .EnsureSuccessStatusCode();

        var response = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "Damaged", null,
                [new ReturnLineDto(order.Items.Single().Id, 1)]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RejectedRma_CannotBeApprovedAfterwards()
    {
        var order = await PlaceOrder("CAP-KHK", 1);
        await Fulfill(order.Number);
        var create = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "Damaged", null,
                [new ReturnLineDto(order.Items.Single().Id, 1)]));
        var rma = await create.Content.ReadFromJsonAsync<ReturnResponse>();

        var admin = await AdminClient();
        (await admin.PostAsJsonAsync($"/api/returns/{rma!.Number}/reject",
            new RejectReturnRequestDto("No"))).EnsureSuccessStatusCode();

        var approve = await admin.PostAsync($"/api/returns/{rma.Number}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
    }

    [Fact]
    public async Task Review_WithoutPurchase_ReturnsExactProblemDetails()
    {
        var customer = _factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, "no-buy@example.com"));
        var product = await _client.GetFromJsonAsync<ProductResponse>(
            "/api/products/by-slug/classic-cotton-tee");

        var response = await customer.PostAsJsonAsync($"/api/products/{product!.Id}/reviews",
            new CreateReviewRequest(5, "Great", "Never actually bought it though."));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Only verified purchasers can review this product.",
            problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Review_AfterPartialFulfillment_IsStillAllowed()
    {
        // Paying grants the review right; a partial shipment moving the order
        // to PartiallyFulfilled must not silently revoke it.
        var customer = _factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, "partial-buyer@example.com"));
        var order = await PlaceOrder("TEE-BLK-S", 2, customer, "partial-buyer@example.com");

        var admin = await AdminClient();
        var partial = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest(null, null,
                [new FulfillmentLineDto(order.Items.Single().Id, 1)]));
        Assert.Equal(HttpStatusCode.Created, partial.StatusCode);
        var partiallyFulfilled = await customer.GetFromJsonAsync<OrderResponse>(
            $"/api/orders/{order.Number}");
        Assert.Equal("PartiallyFulfilled", partiallyFulfilled!.Status);

        var product = await _client.GetFromJsonAsync<ProductResponse>(
            "/api/products/by-slug/classic-cotton-tee");
        var response = await customer.PostAsJsonAsync($"/api/products/{product!.Id}/reviews",
            new CreateReviewRequest(4, "Half arrived", "The first tee is great."));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<HttpClient> AdminClient()
    {
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        return admin;
    }

    private async Task Fulfill(string orderNumber)
    {
        var admin = await AdminClient();
        (await admin.PostAsync($"/api/orders/{orderNumber}/fulfill", null))
            .EnsureSuccessStatusCode();
    }

    private async Task<OrderResponse> PlaceOrder(
        string sku, int quantity, HttpClient? client = null, string email = GuestEmail)
    {
        client ??= _client;
        var cartResponse = await client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var inventory = await client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        (await client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, quantity)))
            .EnsureSuccessStatusCode();

        var checkout = await client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, email, Address, null, "tok_visa"));
        checkout.EnsureSuccessStatusCode();
        var receipt = (await checkout.Content.ReadFromJsonAsync<CheckoutResponse>())!;
        if (receipt.GuestOrderAccessToken is not null)
        {
            client.DefaultRequestHeaders.Remove("X-Agora-Order-Access");
            client.DefaultRequestHeaders.Add("X-Agora-Order-Access", receipt.GuestOrderAccessToken);
        }
        return System.Text.Json.JsonSerializer.Deserialize<OrderResponse>(
            System.Text.Json.JsonSerializer.Serialize(receipt))!;
    }
}
