using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

/// <summary>
/// Refund correctness: every tender returns to its source (gateway charge
/// first, then gift card), RMA proration composes with discounts and tax, and
/// the over-refund guard holds at its exact boundary.
/// </summary>
public class RefundTenderTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private const string GuestEmail = "tender@example.com";

    private static readonly AddressDto Address = new(
        "Ten Der", "2 Payback Pl", null, "Refundton", "VA", "22201", "US");

    [Fact]
    public async Task CancellingPaidOrder_ReturnsGiftCardTender()
    {
        var card = await IssueCard(10m);
        var order = await PlaceOrder([("TEE-BLK-S", 2)], giftCardCode: card); // 10 gift + 39.17 card
        Assert.Equal(0m, await Balance(card));

        (await _client.PostAsync($"/api/orders/{order.Number}/cancel", null))
            .EnsureSuccessStatusCode();

        Assert.Equal(10m, await Balance(card));
    }

    [Fact]
    public async Task RmaOnFullyGiftCardPaidOrder_CreditsCard_NotGateway()
    {
        var card = await IssueCard(100m);
        var order = await PlaceOrder([("TEE-BLK-S", 2)], giftCardCode: card); // total 49.17, all gift
        Assert.Equal(50.83m, await Balance(card));
        await Fulfill(order.Number);

        // 1 of 2 tees back: 19.99 + tax share = 21.59, all of it gift tender.
        var rma = await CreateRma(order.Number, order.Items.Single().Id, 1);
        var approved = await Approve(rma.Number);

        Assert.Equal(21.59m, approved.RefundAmount);
        // No gateway transaction was ever charged, so none may be refunded.
        Assert.StartsWith("gcref_", approved.RefundTransactionId);
        Assert.Equal(72.42m, await Balance(card)); // 50.83 + 21.59
    }

    [Fact]
    public async Task SequentialRmas_MixedTender_DrainGatewayFirst_ThenCreditCard()
    {
        var card = await IssueCard(10m);
        // Total 49.17 = 39.17 gateway charge + 10.00 gift card.
        var order = await PlaceOrder([("TEE-BLK-S", 2)], giftCardCode: card);
        await Fulfill(order.Number);
        var itemId = order.Items.Single().Id;

        // First unit (21.59) fits inside the 39.17 gateway charge entirely.
        var first = await Approve((await CreateRma(order.Number, itemId, 1)).Number);
        Assert.StartsWith("rfnd_", first.RefundTransactionId);
        Assert.Equal(0m, await Balance(card));

        // Second unit: 17.58 remains on the gateway, the last 4.01 is gift
        // tender and must land back on the card.
        var second = await Approve((await CreateRma(order.Number, itemId, 1)).Number);
        Assert.Equal(21.59m, second.RefundAmount);
        Assert.StartsWith("rfnd_", second.RefundTransactionId);
        Assert.Equal(4.01m, await Balance(card));
    }

    [Fact]
    public async Task FullRefund_AfterPartialFulfillment_ReturnsBothTenders()
    {
        var card = await IssueCard(20m);
        var order = await PlaceOrder([("TEE-BLK-S", 2)], giftCardCode: card); // 20 gift + 29.17 card
        Assert.Equal(0m, await Balance(card));

        // Ship only one unit, then refund the whole order.
        var admin = await AdminClient();
        var partial = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest("UPS", "1Z-PART", [
                new FulfillmentLineDto(order.Items.Single().Id, 1)]));
        Assert.Equal(HttpStatusCode.Created, partial.StatusCode);

        var refund = await _client.PostAsync($"/api/orders/{order.Number}/refund", null);

        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        var refunded = await refund.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal("Refunded", refunded!.Status);
        Assert.Equal(20m, await Balance(card));
    }

    [Fact]
    public async Task OverRefundGuard_ExactRemainingQuantitySucceeds_OneMoreFails()
    {
        var order = await PlaceOrder([("TEE-BLK-S", 3)]);
        await Fulfill(order.Number);
        var itemId = order.Items.Single().Id;

        await CreateRma(order.Number, itemId, 2);

        // Exactly the remaining unit is fine; anything further must 422 even
        // though the earlier RMA is still only Requested.
        var atBoundary = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "Damaged", null, [new ReturnLineDto(itemId, 1)]));
        Assert.Equal(HttpStatusCode.Created, atBoundary.StatusCode);

        var beyond = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "Damaged", null, [new ReturnLineDto(itemId, 1)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, beyond.StatusCode);
    }

    [Fact]
    public async Task MultiLineRma_WithDiscount_ProratesRefundPerLine()
    {
        // Subtotal 68.98, WELCOME10 -> discount 6.90, tax 4.97, free shipping.
        var order = await PlaceOrder([("TEE-BLK-S", 2), ("CDL-CDR-S", 2)], discount: "WELCOME10");
        await Fulfill(order.Number);
        var tee = order.Items.Single(i => i.Sku == "TEE-BLK-S");
        var candle = order.Items.Single(i => i.Sku == "CDL-CDR-S");

        var response = await _client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(GuestEmail, "NotAsDescribed", null,
            [
                new ReturnLineDto(tee.Id, 1),
                new ReturnLineDto(candle.Id, 1),
            ]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rma = await response.Content.ReadFromJsonAsync<ReturnResponse>();
        // Each line refunds unit price minus its discount share plus its tax
        // share: 19.99 -> 19.43 and 14.50 -> 14.09.
        Assert.Equal(19.43m, rma!.Items.Single(i => i.Sku == "TEE-BLK-S").RefundAmount);
        Assert.Equal(14.09m, rma.Items.Single(i => i.Sku == "CDL-CDR-S").RefundAmount);
        Assert.Equal(33.52m, rma.RefundAmount);
    }

    [Fact]
    public async Task ApprovedRmas_NeverExceedOrderLineValue_WhenEveryUnitComesBack()
    {
        // Return everything across two RMAs: the summed refunds must equal the
        // order's discounted merchandise value plus tax (shipping is kept).
        var order = await PlaceOrder([("TEE-BLK-S", 2)]); // 39.98 + 3.20 tax + 5.99 ship = 49.17
        await Fulfill(order.Number);
        var itemId = order.Items.Single().Id;

        var first = await Approve((await CreateRma(order.Number, itemId, 1)).Number);
        var second = await Approve((await CreateRma(order.Number, itemId, 1)).Number);

        var merchandiseWithTax = order.Subtotal - order.DiscountAmount + order.TaxAmount;
        Assert.Equal(43.18m, merchandiseWithTax);
        Assert.Equal(merchandiseWithTax, first.RefundAmount + second.RefundAmount);
    }

    private async Task<HttpClient> AdminClient()
    {
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        return admin;
    }

    private async Task<string> IssueCard(decimal amount)
    {
        var admin = await AdminClient();
        var response = await admin.PostAsJsonAsync("/api/gift-cards",
            new IssueGiftCardRequest(amount, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GiftCardResponse>())!.Code;
    }

    private async Task<decimal> Balance(string card) =>
        (await _client.GetFromJsonAsync<GiftCardResponse>($"/api/gift-cards/{card}"))!.Balance;

    private async Task Fulfill(string orderNumber)
    {
        var admin = await AdminClient();
        (await admin.PostAsync($"/api/orders/{orderNumber}/fulfill", null))
            .EnsureSuccessStatusCode();
    }

    private async Task<ReturnResponse> CreateRma(string orderNumber, Guid orderItemId, int quantity)
    {
        var response = await _client.PostAsJsonAsync($"/api/orders/{orderNumber}/returns",
            new CreateReturnRequestDto(GuestEmail, "Damaged", null,
                [new ReturnLineDto(orderItemId, quantity)]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReturnResponse>())!;
    }

    private async Task<ReturnResponse> Approve(string rmaNumber)
    {
        var admin = await AdminClient();
        var response = await admin.PostAsync($"/api/returns/{rmaNumber}/approve", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReturnResponse>())!;
    }

    private async Task<OrderResponse> PlaceOrder(
        (string Sku, int Quantity)[] lines, string? discount = null, string? giftCardCode = null)
    {
        var cartResponse = await _client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        foreach (var (sku, quantity) in lines)
        {
            var inventory = await _client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
            (await _client.PostAsJsonAsync($"/api/carts/{token}/items",
                new AddCartItemRequest(inventory!.ProductVariantId, quantity)))
                .EnsureSuccessStatusCode();
        }

        var checkout = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, GuestEmail, Address, discount, "tok_visa",
                null, null, giftCardCode));
        checkout.EnsureSuccessStatusCode();
        return (await checkout.Content.ReadFromJsonAsync<OrderResponse>())!;
    }
}
