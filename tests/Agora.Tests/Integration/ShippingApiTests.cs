using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class ShippingApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly AddressDto Address = new(
        "Ada Lovelace", "1 Analytical Way", null, "London", "LDN", "EC1A 1AA", "GB");

    [Fact]
    public async Task List_ReturnsActiveSeededMethods()
    {
        var methods = await _client.GetFromJsonAsync<List<ShippingMethodResponse>>("/api/shipping-methods");

        Assert.NotNull(methods);
        Assert.Contains(methods, m => m.Code == "standard" && m.IsDefault);
        Assert.Contains(methods, m => m.Code == "express");
        Assert.Contains(methods, m => m.Code == "freight" && m.RateType == "Weighted");
    }

    [Fact]
    public async Task Checkout_WithoutMethod_UsesDefaultStandard()
    {
        var order = await Checkout("TEE-BLK-S", 2); // 39.98, under free threshold

        Assert.Equal("standard", order.ShippingMethodCode);
        Assert.Equal("Standard Shipping", order.ShippingMethodName);
        Assert.Equal(5.99m, order.ShippingAmount);
        Assert.NotNull(order.EstimatedDeliveryFrom);
        Assert.NotNull(order.EstimatedDeliveryTo);
        Assert.True(order.EstimatedDeliveryTo >= order.EstimatedDeliveryFrom);
    }

    [Fact]
    public async Task Checkout_Express_ChargesExpressRate_NoFreeThreshold()
    {
        // 129.99 would ship free on standard, but express has no threshold.
        var order = await Checkout("EAR-AUR-BLK", 1, methodCode: "express");

        Assert.Equal("express", order.ShippingMethodCode);
        Assert.Equal(14.99m, order.ShippingAmount);
        // 129.99 + 10.40 tax + 14.99 shipping
        Assert.Equal(155.38m, order.Total);
    }

    [Fact]
    public async Task Checkout_Freight_ChargesByWeight()
    {
        // 2 x TEE-BLK-S @ 180g = 360g -> 4.99 + 2.00 * 0.36 = 5.71
        var order = await Checkout("TEE-BLK-S", 2, methodCode: "freight");

        Assert.Equal("freight", order.ShippingMethodCode);
        Assert.Equal(5.71m, order.ShippingAmount);
    }

    [Fact]
    public async Task Checkout_UnknownMethod_Returns422()
    {
        var token = await CartWith("CAP-KHK", 1);

        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "a@example.com", Address, null, "tok_visa", "teleport"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_InactiveMethod_Returns422()
    {
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        var create = await admin.PostAsJsonAsync("/api/shipping-methods",
            new CreateShippingMethodRequest(
                "retired", "Retired Courier", "Flat", 9.99m, 0m, null, 2, 4, false, null));
        create.EnsureSuccessStatusCode();

        var token = await CartWith("CAP-KHK", 1);
        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "a@example.com", Address, null, "tok_visa", "retired"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_MissingAddress_Returns400()
    {
        var token = await CartWith("CAP-KHK", 1);

        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "a@example.com", null, null, "tok_visa"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_WithSavedAddress_UsesAddressBookEntry()
    {
        var client = _factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, "saved-addr@example.com"));
        var saved = await (await client.PostAsJsonAsync("/api/me/addresses",
            new SaveAddressRequest("Home", Address, null))).Content
            .ReadFromJsonAsync<CustomerAddressResponse>();

        var token = await CartWith("CAP-KHK", 1, client);
        var response = await client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "saved-addr@example.com", null, null, "tok_visa",
                null, saved!.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal("1 Analytical Way", order!.ShippingAddress.Line1);
        Assert.Equal("Ada Lovelace", order.ShippingAddress.FullName);
    }

    [Fact]
    public async Task Checkout_WithAnotherCustomersSavedAddress_Returns404()
    {
        var alice = _factory.CreateClient();
        alice.UseBearer(await TestAuth.RegisterAsync(alice, "addr-owner@example.com"));
        var saved = await (await alice.PostAsJsonAsync("/api/me/addresses",
            new SaveAddressRequest("Home", Address, null))).Content
            .ReadFromJsonAsync<CustomerAddressResponse>();

        var mallory = _factory.CreateClient();
        mallory.UseBearer(await TestAuth.RegisterAsync(mallory, "addr-thief@example.com"));
        var token = await CartWith("CAP-KHK", 1, mallory);
        var response = await mallory.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "addr-thief@example.com", null, null, "tok_visa",
                null, saved!.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_GuestWithSavedAddressId_Returns400()
    {
        var token = await CartWith("CAP-KHK", 1);

        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "guest@example.com", null, null, "tok_visa",
                null, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminCanCreateMethod_CustomerCannot()
    {
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        var created = await admin.PostAsJsonAsync("/api/shipping-methods",
            new CreateShippingMethodRequest(
                "overnight", "Overnight", "Flat", 24.99m, 0m, null, 1, 1, null, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var customer = _factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, "not-admin@example.com"));
        var forbidden = await customer.PostAsJsonAsync("/api/shipping-methods",
            new CreateShippingMethodRequest(
                "sneaky", "Sneaky", "Flat", 0m, 0m, null, 1, 1, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var anonymous = await _client.PostAsJsonAsync("/api/shipping-methods",
            new CreateShippingMethodRequest(
                "anon", "Anon", "Flat", 0m, 0m, null, 1, 1, null, null));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Create_NewDefault_ClearsExistingDefault()
    {
        // Use a dedicated factory so we don't disturb other tests' default method.
        using var localFactory = new AgoraApiFactory();
        var admin = localFactory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        var response = await admin.PostAsJsonAsync("/api/shipping-methods",
            new CreateShippingMethodRequest(
                "eco", "Eco Saver", "Flat", 2.99m, 0m, null, 7, 14, null, true));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var methods = await admin.GetFromJsonAsync<List<ShippingMethodResponse>>("/api/shipping-methods");
        Assert.Single(methods!, m => m.IsDefault);
        Assert.True(methods!.Single(m => m.Code == "eco").IsDefault);
    }

    private async Task<string> CartWith(string sku, int quantity, HttpClient? client = null)
    {
        client ??= _client;
        var cartResponse = await client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var inventory = await client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        var add = await client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, quantity));
        add.EnsureSuccessStatusCode();
        return token;
    }

    private async Task<OrderResponse> Checkout(string sku, int quantity, string? methodCode = null)
    {
        var token = await CartWith(sku, quantity);
        var response = await _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "shipper@example.com", Address, null, "tok_visa", methodCode));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }
}
