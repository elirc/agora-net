using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

/// <summary>
/// Invariants of the totals pipeline (discounts -> tax -> gift card tender):
/// per-category tax on discounted lines, rounding cent-conservation, zero- and
/// clamped-total guards, and the free-shipping threshold boundary.
/// </summary>
public class TotalsPipelineTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly AddressDto UsAddress = new(
        "Sum Checker", "1 Ledger Ln", null, "Centville", "VA", "22201", "US");
    private static readonly AddressDto GbAddress = new(
        "V A Tperson", "10 Downing St", null, "London", "LDN", "SW1A 2AA", "GB");
    private static readonly AddressDto DeAddress = new(
        "Steuer Frei", "Bahnhofstr. 1", null, "Berlin", "BE", "10115", "DE");

    [Fact]
    public async Task PercentDiscount_MultiCategoryTax_TaxesEachDiscountedLineAtItsRate()
    {
        var admin = await AdminClient();
        await CreateProduct(admin, "Booster Cushion", "booster-cushion", "MIX-RED", 100m, "reduced");
        await CreateProduct(admin, "Plain Loaf", "plain-loaf", "MIX-ZERO", 40m, "zero");

        // GB zone: standard 8%, reduced 5%, zero 0%. WELCOME10 discounts every
        // line by 10% before tax, so each category taxes its discounted amount.
        var token = await CartWith(("MIX-RED", 1), ("MIX-ZERO", 1), ("TEE-BLK-S", 1));
        var order = await Checkout(token, GbAddress, discount: "WELCOME10");

        Assert.Equal(159.99m, order.Subtotal);
        Assert.Equal(16.00m, order.DiscountAmount); // 15.999 rounds away from zero
        // 90.00 * 5% + 36.00 * 0% + 17.99 * 8% (all on discounted lines) = 5.94
        Assert.Equal(5.94m, order.TaxAmount);
        Assert.Equal(0m, order.ShippingAmount); // 143.99 discounted >= 50 threshold
        Assert.Equal(149.93m, order.Total);
        AssertTotalsIdentity(order);
    }

    [Fact]
    public async Task FixedDiscount_ProratesAcrossTaxCategories()
    {
        var admin = await AdminClient();
        await CreateProduct(admin, "Zero Rated Beans", "zero-rated-beans", "ZR-BEANS", 30m, "zero");

        // SAVE5 is prorated across both lines; only the standard-rated tee line
        // is taxed, on its discounted share (19.99 * (1 - 5/49.99) * 8% = 1.44).
        var token = await CartWith(("ZR-BEANS", 1), ("TEE-BLK-S", 1));
        var order = await Checkout(token, UsAddress, discount: "SAVE5");

        Assert.Equal(49.99m, order.Subtotal);
        Assert.Equal(5.00m, order.DiscountAmount);
        Assert.Equal(1.44m, order.TaxAmount);
        Assert.Equal(5.99m, order.ShippingAmount); // 44.99 discounted < 50 threshold
        Assert.Equal(52.42m, order.Total);
        AssertTotalsIdentity(order);
    }

    [Fact]
    public async Task DiscountLargerThanSubtotal_ClampsToZero_ShippingStillCharged()
    {
        var admin = await AdminClient();
        await CreateDiscount(admin, "MEGASAVE", "FixedAmount", 1000m);

        var token = await CartWith(("CDL-CDR-S", 1)); // 14.50
        var order = await Checkout(token, UsAddress, discount: "MEGASAVE");

        // The discount clamps at the subtotal; nothing taxable remains, but the
        // (now sub-threshold) shipping charge still applies and is card-charged.
        Assert.Equal(14.50m, order.Subtotal);
        Assert.Equal(14.50m, order.DiscountAmount);
        Assert.Equal(0m, order.TaxAmount);
        Assert.Equal(5.99m, order.ShippingAmount);
        Assert.Equal(5.99m, order.Total);
        Assert.StartsWith("txn_", order.PaymentTransactionId);
        AssertTotalsIdentity(order);
    }

    [Fact]
    public async Task HundredPercentDiscount_ZeroTotalOrder_SucceedsWithoutGateway()
    {
        var admin = await AdminClient();
        await CreateDiscount(admin, "FREEBIE", "Percentage", 100m);
        var pickup = await admin.PostAsJsonAsync("/api/shipping-methods",
            new CreateShippingMethodRequest(
                "pickup", "Store Pickup", "Flat", 0m, 0m, null, 0, 0, true, null));
        Assert.Equal(HttpStatusCode.Created, pickup.StatusCode);

        // 100% discount + no tax zone (DE) + free pickup = a genuinely zero
        // total; the gateway must be skipped, not charged 0.00 or crashed into.
        var before = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/TEE-BLK-S"))!;
        var token = await CartWith(("TEE-BLK-S", 1));
        var order = await Checkout(token, DeAddress,
            discount: "FREEBIE", shippingMethodCode: "pickup", paymentToken: "tok_fail");

        Assert.Equal("Paid", order.Status);
        Assert.Equal(0m, order.Total);
        Assert.Equal(0m, order.GiftCardAmount);
        Assert.StartsWith("free_", order.PaymentTransactionId);
        AssertTotalsIdentity(order);

        // Stock committed like any paid order.
        var after = (await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/TEE-BLK-S"))!;
        Assert.Equal(before.QuantityOnHand - 1, after.QuantityOnHand);
        Assert.Equal(before.QuantityReserved, after.QuantityReserved);
    }

    [Fact]
    public async Task FreeShipping_DiscountedSubtotalExactlyAtThreshold_ShipsFree()
    {
        var admin = await AdminClient();
        await CreateDiscount(admin, "SAVE450", "FixedAmount", 4.50m);

        // 54.50 - 4.50 lands exactly on the 50.00 threshold (inclusive).
        var token = await CartWith(("HOOD-GRY-M", 1));
        var order = await Checkout(token, UsAddress, discount: "SAVE450");

        Assert.Equal(0m, order.ShippingAmount);
        Assert.Equal(4.00m, order.TaxAmount); // 8% of exactly 50.00
        Assert.Equal(54.00m, order.Total);
        AssertTotalsIdentity(order);
    }

    [Fact]
    public async Task FreeShipping_OneCentBelowThreshold_ChargesShipping()
    {
        var admin = await AdminClient();
        await CreateDiscount(admin, "SAVE451", "FixedAmount", 4.51m);

        // 54.50 - 4.51 = 49.99: one cent under the threshold pays full freight.
        var token = await CartWith(("HOOD-GRY-M", 1));
        var order = await Checkout(token, UsAddress, discount: "SAVE451");

        Assert.Equal(5.99m, order.ShippingAmount);
        Assert.Equal(4.00m, order.TaxAmount); // 3.9992 rounds up
        Assert.Equal(59.98m, order.Total);
        AssertTotalsIdentity(order);
    }

    [Fact]
    public async Task GiftCard_ExactlyEqualToTotal_SkipsGateway_AndDrainsToZero()
    {
        var card = await IssueCard(49.17m); // TEE-BLK-S x2 US total, to the cent

        // The boundary case between partial tender and full coverage: an exact
        // match must not send a 0.00 charge to the gateway (tok_fail proves it).
        var token = await CartWith(("TEE-BLK-S", 2));
        var order = await Checkout(token, UsAddress, giftCardCode: card, paymentToken: "tok_fail");

        Assert.Equal(49.17m, order.Total);
        Assert.Equal(49.17m, order.GiftCardAmount);
        Assert.StartsWith("gift_", order.PaymentTransactionId);
        AssertTotalsIdentity(order);

        var balance = await _client.GetFromJsonAsync<GiftCardResponse>($"/api/gift-cards/{card}");
        Assert.Equal(0m, balance!.Balance);
    }

    [Fact]
    public async Task PercentDiscount_RoundingConservesCents_AcrossLines()
    {
        // 74.47 * 10% = 7.447 forces a rounding decision; the pipeline must
        // keep subtotal - discount + tax + shipping equal to the stored total.
        var token = await CartWith(("TEE-BLK-S", 3), ("CDL-CDR-S", 1));
        var order = await Checkout(token, UsAddress, discount: "WELCOME10");

        Assert.Equal(74.47m, order.Subtotal);
        Assert.Equal(7.45m, order.DiscountAmount); // 7.447 rounds away from zero
        Assert.Equal(5.36m, order.TaxAmount);      // 8% of 67.02, rounded once
        Assert.Equal(0m, order.ShippingAmount);    // 67.02 >= 50
        Assert.Equal(72.38m, order.Total);
        AssertTotalsIdentity(order);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("WELCOME10", false)]
    [InlineData("WELCOME10", true)]
    [InlineData("SAVE5", false)]
    [InlineData("SAVE5", true)]
    public async Task TotalsIdentity_HoldsAcrossDiscountAndTenderCombinations(
        string? discount, bool useGiftCard)
    {
        var card = useGiftCard ? await IssueCard(10m) : null;

        var token = await CartWith(("CAP-KHK", 2)); // 48.00
        var order = await Checkout(token, UsAddress, discount: discount, giftCardCode: card);

        AssertTotalsIdentity(order);
        Assert.True(order.DiscountAmount >= 0 && order.TaxAmount >= 0
            && order.ShippingAmount >= 0 && order.Total >= 0);

        if (card is null)
        {
            Assert.Equal(0m, order.GiftCardAmount);
        }
        else
        {
            // Tender is applied last, against the final total, never beyond it.
            Assert.Equal(Math.Min(10m, order.Total), order.GiftCardAmount);
            var balance = await _client.GetFromJsonAsync<GiftCardResponse>($"/api/gift-cards/{card}");
            Assert.Equal(10m - order.GiftCardAmount, balance!.Balance);
        }
    }

    private static void AssertTotalsIdentity(OrderResponse order) =>
        Assert.Equal(
            order.Subtotal - order.DiscountAmount + order.TaxAmount + order.ShippingAmount,
            order.Total);

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

    private static async Task CreateDiscount(
        HttpClient admin, string code, string type, decimal value)
    {
        var response = await admin.PostAsJsonAsync("/api/discounts",
            new CreateDiscountRequest(code, type, value, null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task CreateProduct(
        HttpClient admin, string name, string slug, string sku, decimal price, string taxCategory)
    {
        var categories = (await _client.GetFromJsonAsync<PagedResult<CategoryResponse>>(
            "/api/categories"))!.Items;
        var response = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest(
            categories![0].Id, name, slug, null, null,
            [new CreateVariantRequest(sku, null, price, null, null)], null, taxCategory));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        (await admin.PutAsJsonAsync($"/api/inventory/{sku}", new SetStockRequest(50)))
            .EnsureSuccessStatusCode();
    }

    private async Task<string> CartWith(params (string Sku, int Quantity)[] lines)
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

        return token;
    }

    private async Task<OrderResponse> Checkout(
        string token, AddressDto address, string? discount = null, string? giftCardCode = null,
        string? shippingMethodCode = null, string paymentToken = "tok_visa")
    {
        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "totals@example.com", address, discount, paymentToken,
                shippingMethodCode, null, giftCardCode));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }
}
