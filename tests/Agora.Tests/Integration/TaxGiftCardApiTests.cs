using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class TaxGiftCardApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly AddressDto UsAddress = new(
        "Tax Payer", "1 Ledger Ln", null, "Booktown", "VA", "22201", "US");
    private static readonly AddressDto DeAddress = new(
        "Steuer Frei", "Bahnhofstr. 1", null, "Berlin", "BE", "10115", "DE");
    private static readonly AddressDto CaAddress = new(
        "Golden State", "1 Beach Blvd", null, "Los Angeles", "CA", "90001", "US");
    private static readonly AddressDto GbAddress = new(
        "V A Tperson", "10 Downing St", null, "London", "LDN", "SW1A 2AA", "GB");

    [Fact]
    public async Task Checkout_NoMatchingZone_ChargesNoTax()
    {
        var order = await Checkout("TEE-BLK-S", 2, DeAddress);

        Assert.Equal(0m, order.TaxAmount);
        Assert.Equal(45.97m, order.Total); // 39.98 + 5.99 shipping
    }

    [Fact]
    public async Task Checkout_CountryZone_KeepsEightPercent()
    {
        var order = await Checkout("TEE-BLK-S", 2, UsAddress);

        Assert.Equal(3.20m, order.TaxAmount); // 8% of 39.98
        Assert.Equal(49.17m, order.Total);
    }

    [Fact]
    public async Task RegionZone_BeatsCountryZone()
    {
        var admin = await AdminClient();
        var create = await admin.PostAsJsonAsync("/api/tax-zones",
            new SaveTaxZoneRequest("us-ca", "California", "US", "CA", 0.095m, null, null));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var order = await Checkout("TEE-BLK-S", 1, CaAddress);

        Assert.Equal(1.90m, order.TaxAmount); // 9.5% of 19.99 = 1.89905
    }

    [Fact]
    public async Task ReducedRateCategory_UsesZoneOverride()
    {
        var admin = await AdminClient();
        var categories = (await _client.GetFromJsonAsync<PagedResult<CategoryResponse>>("/api/categories"))!.Items;

        // A GB "reduced" (5%) product.
        var create = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest(
            categories![0].Id, "Kids Booster Seat", "kids-booster-seat", null, null,
            [new CreateVariantRequest("SEAT-KID", null, 100m, null, null)], null, "reduced"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var product = await create.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal("reduced", product!.TaxCategoryCode);
        (await admin.PutAsJsonAsync("/api/inventory/SEAT-KID", new SetStockRequest(10)))
            .EnsureSuccessStatusCode();

        var order = await Checkout("SEAT-KID", 1, GbAddress);

        Assert.Equal(5.00m, order.TaxAmount); // 5% of 100, not the 8% default
    }

    [Fact]
    public async Task UnknownTaxCategory_OnProduct_Returns422()
    {
        var admin = await AdminClient();
        var categories = (await _client.GetFromJsonAsync<PagedResult<CategoryResponse>>("/api/categories"))!.Items;

        var create = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest(
            categories![0].Id, "Mystery Tax", "mystery-tax", null, null,
            [new CreateVariantRequest("MYST-1", null, 10m, null, null)], null, "luxury"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, create.StatusCode);
    }

    [Fact]
    public async Task TaxZones_MutationsAreAdminOnly()
    {
        var anonymous = await _client.PostAsJsonAsync("/api/tax-zones",
            new SaveTaxZoneRequest("x", "X", "FR", null, 0.2m, null, null));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var reads = await _client.GetAsync("/api/tax-zones");
        Assert.Equal(HttpStatusCode.OK, reads.StatusCode);
    }

    [Fact]
    public async Task GiftCard_IssueAndBalanceCheck()
    {
        var admin = await AdminClient();

        var issue = await admin.PostAsJsonAsync("/api/gift-cards",
            new IssueGiftCardRequest(75m, null, null));
        Assert.Equal(HttpStatusCode.Created, issue.StatusCode);
        var card = await issue.Content.ReadFromJsonAsync<GiftCardResponse>();
        Assert.StartsWith("GC-", card!.Code);
        Assert.Equal(75m, card.Balance);

        var balance = await _client.GetFromJsonAsync<GiftCardResponse>($"/api/gift-cards/{card.Code}");
        Assert.Equal(75m, balance!.Balance);
    }

    [Fact]
    public async Task GiftCard_IssueIsAdminOnly()
    {
        var customer = _factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, "gc-pleb@example.com"));

        var anonymous = await _client.PostAsJsonAsync("/api/gift-cards",
            new IssueGiftCardRequest(10m, null, null));
        var forbidden = await customer.PostAsJsonAsync("/api/gift-cards",
            new IssueGiftCardRequest(10m, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Checkout_PartialGiftCard_ChargesRemainderAndDrainsCard()
    {
        var card = await IssueCard(10m);

        // 2 tees: total 49.17 -> 10 gift card + 39.17 gateway charge.
        var order = await Checkout("TEE-BLK-S", 2, UsAddress, giftCardCode: card);

        Assert.Equal(card, order.GiftCardCode);
        Assert.Equal(10m, order.GiftCardAmount);
        Assert.Equal(49.17m, order.Total);
        Assert.StartsWith("txn_", order.PaymentTransactionId);

        var balance = await _client.GetFromJsonAsync<GiftCardResponse>($"/api/gift-cards/{card}");
        Assert.Equal(0m, balance!.Balance);
    }

    [Fact]
    public async Task Checkout_FullyCoveredByGiftCard_SkipsGateway()
    {
        var card = await IssueCard(100m);

        // Total 49.17, card covers everything; even a declining token succeeds
        // because the gateway is never called.
        var order = await Checkout("TEE-BLK-S", 2, UsAddress,
            giftCardCode: card, paymentToken: "tok_fail");

        Assert.Equal(49.17m, order.GiftCardAmount);
        Assert.StartsWith("gift_", order.PaymentTransactionId);
        Assert.Equal("Paid", order.Status);

        var balance = await _client.GetFromJsonAsync<GiftCardResponse>($"/api/gift-cards/{card}");
        Assert.Equal(50.83m, balance!.Balance);
    }

    [Fact]
    public async Task Checkout_GiftCardWithDiscount_OrdersDiscountsTaxThenTender()
    {
        var card = await IssueCard(20m);

        // HOOD-GRY-M 54.50, WELCOME10 -> discounted 49.05, tax 3.92,
        // shipping 5.99 (below 50 threshold) -> total 58.96; tender 20.
        var order = await Checkout("HOOD-GRY-M", 1, UsAddress,
            discount: "WELCOME10", giftCardCode: card);

        Assert.Equal(5.45m, order.DiscountAmount);
        Assert.Equal(3.92m, order.TaxAmount);
        Assert.Equal(58.96m, order.Total);
        Assert.Equal(20m, order.GiftCardAmount);
    }

    [Fact]
    public async Task Checkout_UnknownOrUnusableGiftCard_Returns422()
    {
        var unknownToken = await CartWith("CAP-KHK", 1);
        var unknown = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(unknownToken, "gc@example.com", UsAddress, null, "tok_visa",
                null, null, "GC-DOESNOTEXIST"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);

        var admin = await AdminClient();
        var expired = await (await admin.PostAsJsonAsync("/api/gift-cards",
            new IssueGiftCardRequest(25m, null, DateTimeOffset.UtcNow.AddDays(-1)))).Content
            .ReadFromJsonAsync<GiftCardResponse>();
        var expiredToken = await CartWith("CAP-KHK", 1);
        var expiredResponse = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(expiredToken, "gc@example.com", UsAddress, null, "tok_visa",
                null, null, expired!.Code));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, expiredResponse.StatusCode);

        var deactivated = await IssueCard(25m);
        (await admin.PostAsync($"/api/gift-cards/{deactivated}/deactivate", null))
            .EnsureSuccessStatusCode();
        var deactivatedToken = await CartWith("CAP-KHK", 1);
        var deactivatedResponse = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(deactivatedToken, "gc@example.com", UsAddress, null, "tok_visa",
                null, null, deactivated));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, deactivatedResponse.StatusCode);
    }

    [Fact]
    public async Task Checkout_Declined_LeavesGiftCardBalanceUntouched()
    {
        var card = await IssueCard(10m);
        var token = await CartWith("CHG-65W", 1);

        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "gc@example.com", UsAddress, null, "tok_fail",
                null, null, card));

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var balance = await _client.GetFromJsonAsync<GiftCardResponse>($"/api/gift-cards/{card}");
        Assert.Equal(10m, balance!.Balance);
    }

    [Fact]
    public async Task RefundingOrder_CreditsGiftCardBack()
    {
        var card = await IssueCard(10m);
        var order = await Checkout("TEE-BLK-S", 2, UsAddress, giftCardCode: card);
        Assert.Equal(0m, (await _client.GetFromJsonAsync<GiftCardResponse>(
            $"/api/gift-cards/{card}"))!.Balance);

        var refund = await _client.PostAsync($"/api/orders/{order.Number}/refund", null);

        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        var balance = await _client.GetFromJsonAsync<GiftCardResponse>($"/api/gift-cards/{card}");
        Assert.Equal(10m, balance!.Balance);
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

    private async Task<string> CartWith(string sku, int quantity)
    {
        var cartResponse = await _client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var inventory = await _client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        (await _client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, quantity))).EnsureSuccessStatusCode();
        return token;
    }

    private async Task<OrderResponse> Checkout(
        string sku, int quantity, AddressDto address, string? discount = null,
        string? giftCardCode = null, string paymentToken = "tok_visa")
    {
        var token = await CartWith(sku, quantity);
        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "taxpayer@example.com", address, discount, paymentToken,
                null, null, giftCardCode));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }
}
