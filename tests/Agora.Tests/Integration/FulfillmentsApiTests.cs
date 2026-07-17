using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class FulfillmentsApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly AddressDto Address = new(
        "Ship Ment", "8 Depot Dr", null, "Cratertown", "CT", "44444", "US");

    [Fact]
    public async Task PartialFulfillment_SetsPartiallyFulfilled_WithTracking()
    {
        var order = await PlaceOrder("TEE-BLK-M", 3);
        var line = order.Items.Single();
        var admin = await AdminClient();

        var response = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest("UPS", "1Z999AA10123456784",
                [new FulfillmentLineDto(line.Id, 2)]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var fulfillment = await response.Content.ReadFromJsonAsync<FulfillmentResponse>();
        Assert.StartsWith("FUL-", fulfillment!.Number);
        Assert.Equal("UPS", fulfillment.Carrier);
        Assert.Equal("1Z999AA10123456784", fulfillment.TrackingNumber);
        var shipped = Assert.Single(fulfillment.Items);
        Assert.Equal(2, shipped.Quantity);

        var updated = await _client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Number}");
        Assert.Equal("PartiallyFulfilled", updated!.Status);
        Assert.Null(updated.FulfilledAt);
    }

    [Fact]
    public async Task CompletingShipments_DerivesFulfilled()
    {
        var order = await PlaceOrder("TEE-BLK-S", 2);
        var line = order.Items.Single();
        var admin = await AdminClient();

        await Ship(admin, order.Number, line.Id, 1);
        var second = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest("DHL", "JD0002", [new FulfillmentLineDto(line.Id, 1)]));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var updated = await _client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Number}");
        Assert.Equal("Fulfilled", updated!.Status);
        Assert.NotNull(updated.FulfilledAt);

        var list = await _client.GetFromJsonAsync<List<FulfillmentResponse>>(
            $"/api/orders/{order.Number}/fulfillments");
        Assert.Equal(2, list!.Count);
    }

    [Fact]
    public async Task OverShipping_Returns422()
    {
        var order = await PlaceOrder("CAP-KHK", 2);
        var line = order.Items.Single();
        var admin = await AdminClient();
        await Ship(admin, order.Number, line.Id, 1);

        var response = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest("UPS", null, [new FulfillmentLineDto(line.Id, 2)]));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task OmittingItems_ShipsEverythingRemaining()
    {
        var order = await PlaceOrder("KET-EMB-1L", 2);
        var line = order.Items.Single();
        var admin = await AdminClient();
        await Ship(admin, order.Number, line.Id, 1);

        var response = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest("FedEx", "FX123", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var fulfillment = await response.Content.ReadFromJsonAsync<FulfillmentResponse>();
        Assert.Equal(1, fulfillment!.Items.Single().Quantity);

        var updated = await _client.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Number}");
        Assert.Equal("Fulfilled", updated!.Status);
    }

    [Fact]
    public async Task LegacyFulfillEndpoint_CreatesFulfillmentRecord()
    {
        var order = await PlaceOrder("CHG-65W", 1);
        var admin = await AdminClient();

        var response = await admin.PostAsync($"/api/orders/{order.Number}/fulfill", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal("Fulfilled", updated!.Status);

        var list = await _client.GetFromJsonAsync<List<FulfillmentResponse>>(
            $"/api/orders/{order.Number}/fulfillments");
        var fulfillment = Assert.Single(list!);
        Assert.Equal("Manual", fulfillment.Carrier);
    }

    [Fact]
    public async Task FulfillingACancelledOrder_Returns409()
    {
        var order = await PlaceOrder("TEE-WHT-M", 1);
        (await _client.PostAsync($"/api/orders/{order.Number}/cancel", null))
            .EnsureSuccessStatusCode();
        var admin = await AdminClient();

        var response = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest(null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_AfterPartialShipment_Returns409()
    {
        var order = await PlaceOrder("HOOD-GRY-M", 2);
        var line = order.Items.Single();
        var admin = await AdminClient();
        await Ship(admin, order.Number, line.Id, 1);

        var response = await _client.PostAsync($"/api/orders/{order.Number}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Refund_PartiallyFulfilledOrder_RestocksEverything()
    {
        var before = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/KB-NIM-BRN"))!;
        var order = await PlaceOrder("KB-NIM-BRN", 2);
        var line = order.Items.Single();
        var admin = await AdminClient();
        await Ship(admin, order.Number, line.Id, 1);

        var response = await _client.PostAsync($"/api/orders/{order.Number}/refund", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refunded = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal("Refunded", refunded!.Status);

        var after = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/KB-NIM-BRN"))!;
        Assert.Equal(before.QuantityOnHand, after.QuantityOnHand);
    }

    [Fact]
    public async Task Returns_RequireFullFulfillment()
    {
        var order = await PlaceOrder("EAR-AUR-BLK", 2);
        var line = order.Items.Single();
        var admin = await AdminClient();
        await Ship(admin, order.Number, line.Id, 1); // partial only

        var response = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto("shipper@example.com", "Damaged", null,
                [new ReturnLineDto(line.Id, 1)]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task FulfillmentEndpoints_AreAdminOnly()
    {
        var order = await PlaceOrder("CDL-CDR-S", 1);
        var customer = _factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, "shipper-nope@example.com"));

        var anonymous = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest(null, null, null));
        var forbidden = await customer.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest(null, null, null));
        var legacyAnonymous = await _client.PostAsync($"/api/orders/{order.Number}/fulfill", null);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, legacyAnonymous.StatusCode);

        // Reading shipments stays public (order number is the capability).
        var list = await _client.GetAsync($"/api/orders/{order.Number}/fulfillments");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    private async Task<HttpClient> AdminClient()
    {
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        return admin;
    }

    private static async Task Ship(HttpClient admin, string orderNumber, Guid orderItemId, int quantity)
    {
        var response = await admin.PostAsJsonAsync($"/api/orders/{orderNumber}/fulfillments",
            new CreateFulfillmentRequest("UPS", "1Z-TRACK",
                [new FulfillmentLineDto(orderItemId, quantity)]));
        response.EnsureSuccessStatusCode();
    }

    private async Task<OrderResponse> PlaceOrder(string sku, int quantity)
    {
        var cartResponse = await _client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var inventory = await _client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        (await _client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, quantity))).EnsureSuccessStatusCode();

        var checkout = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "shipper@example.com", Address, null, "tok_visa"));
        checkout.EnsureSuccessStatusCode();
        return (await checkout.Content.ReadFromJsonAsync<OrderResponse>())!;
    }
}
