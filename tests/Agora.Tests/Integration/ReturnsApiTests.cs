using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class ReturnsApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private const string GuestEmail = "returner@example.com";

    private static readonly AddressDto Address = new(
        "Ret Urner", "3 Sendback St", null, "Returnia", "RT", "33333", "US");

    [Fact]
    public async Task Create_ForFulfilledGuestOrder_ComputesTaxAdjustedRefund()
    {
        var order = await PlaceFulfilledOrder("TEE-BLK-S", 2); // 2 x 19.99, tax 3.20
        var line = order.Items.Single();

        var response = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "Damaged", "Hole in seam",
                [new ReturnLineDto(line.Id, 1)]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rma = await response.Content.ReadFromJsonAsync<ReturnResponse>();
        Assert.StartsWith("RMA-", rma!.Number);
        Assert.Equal("Requested", rma.Status);
        Assert.Equal("Damaged", rma.Reason);
        Assert.Equal(order.Number, rma.OrderNumber);
        // 19.99 + its share of the 3.20 tax (rate 3.20/39.98) = 21.59
        Assert.Equal(21.59m, rma.RefundAmount);
        var item = Assert.Single(rma.Items);
        Assert.Equal(1, item.Quantity);
    }

    [Fact]
    public async Task Create_WithDiscount_RefundIsDiscountAndTaxAdjusted()
    {
        var order = await PlaceFulfilledOrder("HOOD-GRY-M", 1, discount: "WELCOME10");
        // subtotal 54.50, discount 5.45, tax 3.92 -> full-line refund 49.05 + 3.92 = 52.97
        var itemId = await OrderItemId(order.Number, "HOOD-GRY-M");

        var response = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "NoLongerNeeded", null,
                [new ReturnLineDto(itemId, 1)]));

        var rma = await response.Content.ReadFromJsonAsync<ReturnResponse>();
        Assert.Equal(52.97m, rma!.RefundAmount);
    }

    [Fact]
    public async Task Create_OnPaidUnfulfilledOrder_Returns409()
    {
        var order = await PlaceOrder("CAP-KHK", 1); // Paid, not fulfilled
        var itemId = await OrderItemId(order.Number, "CAP-KHK");

        var response = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "Damaged", null, [new ReturnLineDto(itemId, 1)]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_WrongEmail_Returns404()
    {
        var order = await PlaceFulfilledOrder("CHG-65W", 1);
        var itemId = await OrderItemId(order.Number, "CHG-65W");
        _client.DefaultRequestHeaders.Remove("X-Agora-Order-Access");

        var response = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto("wrong@example.com", "Damaged", null,
                [new ReturnLineDto(itemId, 1)]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_QuantityBeyondPurchased_Returns422()
    {
        var order = await PlaceFulfilledOrder("KET-EMB-1L", 2);
        var itemId = await OrderItemId(order.Number, "KET-EMB-1L");

        var response = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "Damaged", null, [new ReturnLineDto(itemId, 3)]));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_SecondRmaBeyondRemaining_Returns422()
    {
        var order = await PlaceFulfilledOrder("TEE-BLK-M", 2);
        var itemId = await OrderItemId(order.Number, "TEE-BLK-M");

        var first = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "Damaged", null, [new ReturnLineDto(itemId, 2)]));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "Damaged", null, [new ReturnLineDto(itemId, 1)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [Fact]
    public async Task CancellingRma_FreesQuantityForANewOne()
    {
        var order = await PlaceFulfilledOrder("EAR-AUR-WHT", 1);
        var itemId = await OrderItemId(order.Number, "EAR-AUR-WHT");

        var first = await CreateRma(order.Number, itemId, 1);
        (await _client.PostAsJsonAsync($"/api/returns/{first.Number}/cancel",
            new CancelReturnRequestDto(GuestEmail))).EnsureSuccessStatusCode();

        var second = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "Other", null, [new ReturnLineDto(itemId, 1)]));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Approve_RefundsAndRestocks()
    {
        var before = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/KB-NIM-RED"))!;
        var order = await PlaceFulfilledOrder("KB-NIM-RED", 2);
        var itemId = await OrderItemId(order.Number, "KB-NIM-RED");
        var rma = await CreateRma(order.Number, itemId, 1);

        var admin = await AdminClient();
        var response = await admin.PostAsync($"/api/returns/{rma.Number}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = await response.Content.ReadFromJsonAsync<ReturnResponse>();
        Assert.Equal("Approved", approved!.Status);
        Assert.StartsWith("rfnd_", approved.RefundTransactionId);
        Assert.NotNull(approved.ProcessedAt);

        // 2 sold, 1 returned -> net -1 on hand.
        var after = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/KB-NIM-RED"))!;
        Assert.Equal(before.QuantityOnHand - 1, after.QuantityOnHand);
    }

    [Fact]
    public async Task Approve_Twice_Returns409()
    {
        var order = await PlaceFulfilledOrder("CDL-CDR-S", 1);
        var itemId = await OrderItemId(order.Number, "CDL-CDR-S");
        var rma = await CreateRma(order.Number, itemId, 1);

        var admin = await AdminClient();
        (await admin.PostAsync($"/api/returns/{rma.Number}/approve", null)).EnsureSuccessStatusCode();
        var second = await admin.PostAsync($"/api/returns/{rma.Number}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Reject_KeepsStockUntouched_AndTracksNote()
    {
        var before = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/HOOD-GRY-L"))!;
        var order = await PlaceFulfilledOrder("HOOD-GRY-L", 1);
        var itemId = await OrderItemId(order.Number, "HOOD-GRY-L");
        var rma = await CreateRma(order.Number, itemId, 1);

        var admin = await AdminClient();
        var response = await admin.PostAsJsonAsync($"/api/returns/{rma.Number}/reject",
            new RejectReturnRequestDto("Outside the return window"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rejected = await response.Content.ReadFromJsonAsync<ReturnResponse>();
        Assert.Equal("Rejected", rejected!.Status);
        Assert.Equal("Outside the return window", rejected.RejectionNote);

        // Sold 1, nothing restocked.
        var after = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/HOOD-GRY-L"))!;
        Assert.Equal(before.QuantityOnHand - 1, after.QuantityOnHand);

        // Status tracking endpoint reflects the outcome.
        var tracked = await _client.GetFromJsonAsync<ReturnResponse>($"/api/returns/{rma.Number}");
        Assert.Equal("Rejected", tracked!.Status);
    }

    [Fact]
    public async Task FullOrderRefund_AfterApprovedPartialReturn_Returns409()
    {
        var order = await PlaceFulfilledOrder("TEE-WHT-M", 2);
        var itemId = await OrderItemId(order.Number, "TEE-WHT-M");
        var rma = await CreateRma(order.Number, itemId, 1);
        var admin = await AdminClient();
        (await admin.PostAsync($"/api/returns/{rma.Number}/approve", null)).EnsureSuccessStatusCode();

        var refund = await admin.PostAsync($"/api/orders/{order.Number}/refund", null);

        Assert.Equal(HttpStatusCode.Conflict, refund.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_RequireAdminRole()
    {
        var customer = _factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, "rma-nobody@example.com"));

        var queueAnon = await _client.GetAsync("/api/returns");
        var queueCustomer = await customer.GetAsync("/api/returns");
        var approveAnon = await _client.PostAsync("/api/returns/RMA-X/approve", null);
        var approveCustomer = await customer.PostAsync("/api/returns/RMA-X/approve", null);

        Assert.Equal(HttpStatusCode.Unauthorized, queueAnon.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, queueCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, approveAnon.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, approveCustomer.StatusCode);
    }

    [Fact]
    public async Task AdminQueue_FiltersByStatus()
    {
        var order = await PlaceFulfilledOrder("KET-EMB-1L", 1);
        var itemId = await OrderItemId(order.Number, "KET-EMB-1L");
        var rma = await CreateRma(order.Number, itemId, 1);

        var admin = await AdminClient();
        var queue = await admin.GetFromJsonAsync<PagedResult<ReturnResponse>>(
            "/api/returns?status=requested&pageSize=100");

        Assert.Contains(queue!.Items, r => r.Number == rma.Number);
        Assert.All(queue.Items, r => Assert.Equal("Requested", r.Status));
    }

    [Fact]
    public async Task SignedInCustomer_CanCreateAndListOwnReturns()
    {
        var client = _factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, "rma-owner@example.com"));

        var order = await PlaceFulfilledOrder("CHG-65W", 1, client, "rma-owner@example.com");
        var itemId = await OrderItemId(order.Number, "CHG-65W", client);

        // No email needed: matched by account.
        var create = await client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(null, "WrongItem", null, [new ReturnLineDto(itemId, 1)]));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var rma = await create.Content.ReadFromJsonAsync<ReturnResponse>();

        var mine = await client.GetFromJsonAsync<PagedResult<ReturnResponse>>("/api/me/returns");
        Assert.Contains(mine!.Items, r => r.Number == rma!.Number);

        // Someone else's account sees nothing and cannot cancel it.
        var stranger = _factory.CreateClient();
        stranger.UseBearer(await TestAuth.RegisterAsync(stranger, "rma-stranger@example.com"));
        var strangersList = await stranger.GetFromJsonAsync<PagedResult<ReturnResponse>>("/api/me/returns");
        Assert.Empty(strangersList!.Items);
        var cancel = await stranger.PostAsJsonAsync($"/api/returns/{rma!.Number}/cancel",
            new CancelReturnRequestDto(null));
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
    }

    private async Task<HttpClient> AdminClient()
    {
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        return admin;
    }

    private async Task<OrderResponse> PlaceOrder(
        string sku, int quantity, HttpClient? client = null, string email = GuestEmail,
        string? discount = null)
    {
        client ??= _client;
        var cartResponse = await client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var inventory = await client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        (await client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, quantity))).EnsureSuccessStatusCode();

        var checkout = await client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, email, Address, discount, "tok_visa"));
        checkout.EnsureSuccessStatusCode();
        var result=(await checkout.Content.ReadFromJsonAsync<CheckoutResponse>())!;
        if(result.GuestOrderAccessToken is not null){client.DefaultRequestHeaders.Remove("X-Agora-Order-Access");client.DefaultRequestHeaders.Add("X-Agora-Order-Access",result.GuestOrderAccessToken);}
        return System.Text.Json.JsonSerializer.Deserialize<OrderResponse>(
            System.Text.Json.JsonSerializer.Serialize(result))!;
    }

    private async Task<OrderResponse> PlaceFulfilledOrder(
        string sku, int quantity, HttpClient? client = null, string email = GuestEmail,
        string? discount = null)
    {
        var order = await PlaceOrder(sku, quantity, client, email, discount);
        var admin = await AdminClient();
        var fulfill = await admin.PostAsync($"/api/orders/{order.Number}/fulfill", null);
        fulfill.EnsureSuccessStatusCode();
        return (await fulfill.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    private async Task<Guid> OrderItemId(string orderNumber, string sku, HttpClient? client = null)
    {
        var order = await (client ?? _client).GetFromJsonAsync<OrderResponse>($"/api/orders/{orderNumber}");
        return order!.Items.First(i => i.Sku == sku).Id;
    }

    private async Task<ReturnResponse> CreateRma(string orderNumber, Guid orderItemId, int quantity)
    {
        var response = await _client.PostAsJsonAsync($"/api/orders/{orderNumber}/returns",
            new CreateReturnRequestDto(GuestEmail, "Damaged", null,
                [new ReturnLineDto(orderItemId, quantity)]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReturnResponse>())!;
    }
}
