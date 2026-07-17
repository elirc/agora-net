using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class CheckoutApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly AddressDto Address = new(
        "Ada Lovelace", "1 Analytical Way", null, "London", "LDN", "EC1A 1AA", "GB");

    [Fact]
    public async Task Checkout_HappyPath_CreatesPaidOrder_AndCommitsStock()
    {
        var token = await CartWith("TEE-BLK-S", 2); // 2 x 19.99, stock 40

        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "ada@example.com", Address, null, "tok_visa"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        Assert.Equal("Paid", order.Status);
        Assert.StartsWith("ORD-", order.Number);
        Assert.NotNull(order.PaymentTransactionId);
        Assert.Equal(39.98m, order.Subtotal);
        Assert.Equal(0m, order.DiscountAmount);
        Assert.Equal(3.20m, order.TaxAmount);      // 8% of 39.98 = 3.1984
        Assert.Equal(5.99m, order.ShippingAmount); // under free-shipping threshold
        Assert.Equal(49.17m, order.Total);
        var line = Assert.Single(order.Items);
        Assert.Equal("TEE-BLK-S", line.Sku);

        // Stock committed: 40 - 2, nothing left reserved.
        var stock = await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/TEE-BLK-S");
        Assert.Equal(38, stock!.QuantityOnHand);
        Assert.Equal(0, stock.QuantityReserved);

        // Cart emptied.
        var cart = await _client.GetFromJsonAsync<CartResponse>($"/api/carts/{token}");
        Assert.Empty(cart!.Items);
    }

    [Fact]
    public async Task Checkout_WithPercentageDiscount_AdjustsTotals()
    {
        var token = await CartWith("HOOD-GRY-M", 1); // 54.50

        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "ada@example.com", Address, "WELCOME10", "tok_visa"));

        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(54.50m, order!.Subtotal);
        Assert.Equal(5.45m, order.DiscountAmount);  // 10%
        Assert.Equal(3.92m, order.TaxAmount);       // 8% of 49.05 = 3.924
        Assert.Equal(5.99m, order.ShippingAmount);  // 49.05 dips below the free threshold
        Assert.Equal(58.96m, order.Total);
        Assert.Equal("WELCOME10", order.DiscountCode);
    }

    [Fact]
    public async Task Checkout_OverFreeShippingThreshold_ShipsFree()
    {
        var token = await CartWith("EAR-AUR-BLK", 1); // 129.99

        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "ada@example.com", Address, null, "tok_visa"));

        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal(0m, order!.ShippingAmount);
    }

    [Fact]
    public async Task Checkout_ExpiredDiscount_Returns422_WithoutTouchingStock()
    {
        var token = await CartWith("KB-NIM-BRN", 1);
        var before = await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/KB-NIM-BRN");

        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "ada@example.com", Address, "EXPIRED10", "tok_visa"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var after = await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/KB-NIM-BRN");
        Assert.Equal(before!.QuantityOnHand, after!.QuantityOnHand);
        Assert.Equal(before.QuantityReserved, after.QuantityReserved);
    }

    [Fact]
    public async Task Checkout_UnknownDiscount_Returns422()
    {
        var token = await CartWith("TEE-WHT-M", 1);

        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "ada@example.com", Address, "NO-SUCH-CODE", "tok_visa"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_PaymentDeclined_Returns402_AndReleasesReservation()
    {
        var token = await CartWith("CHG-65W", 3);

        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "ada@example.com", Address, null, "tok_fail"));

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);

        // Reservation released, nothing deducted.
        var stock = await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/CHG-65W");
        Assert.Equal(80, stock!.QuantityOnHand);
        Assert.Equal(0, stock.QuantityReserved);

        // Cart preserved so the shopper can retry.
        var cart = await _client.GetFromJsonAsync<CartResponse>($"/api/carts/{token}");
        Assert.Single(cart!.Items);
    }

    [Fact]
    public async Task Checkout_EmptyCart_Returns400()
    {
        var cart = await CreateCart();

        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(cart, "ada@example.com", Address, null, "tok_visa"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_UnknownCart_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest("ghost-token", "ada@example.com", Address, null, "tok_visa"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_InvalidEmail_Returns400()
    {
        var token = await CartWith("CAP-KHK", 1);

        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "not-an-email", Address, null, "tok_visa"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_UsageLimitedDiscount_SecondUseRejected()
    {
        var create = await _client.PostAsJsonAsync("/api/discounts",
            new CreateDiscountRequest("ONCE5", "FixedAmount", 5m, null, null, 1, null));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var firstCart = await CartWith("TEE-BLK-M", 1);
        var first = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(firstCart, "a@example.com", Address, "ONCE5", "tok_visa"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var secondCart = await CartWith("TEE-BLK-M", 1);
        var second = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(secondCart, "b@example.com", Address, "ONCE5", "tok_visa"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
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
        var variantId = await GetVariantId(sku);
        var response = await _client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(variantId, quantity));
        response.EnsureSuccessStatusCode();
        return token;
    }

    private async Task<Guid> GetVariantId(string sku)
    {
        var inventory = await _client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        return inventory!.ProductVariantId;
    }
}
