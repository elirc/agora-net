using System.Net;
using System.Net.Http.Json;
using System.Text;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

/// <summary>
/// Authorization matrix: every admin-gated endpoint must 401 anonymous callers
/// and 403 signed-in customers, and customer-owned resources must be invisible
/// across accounts.
/// </summary>
public class AuthzMatrixTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";

    /// <summary>Every admin-only endpoint: method, path, whether to send a JSON body.</summary>
    private static readonly (string Method, string Path, bool HasBody)[] AdminEndpoints =
    [
        ("PUT", "/api/inventory/TEE-BLK-S", true),
        ("POST", "/api/products", true),
        ("PUT", $"/api/products/{EmptyGuid}", true),
        ("DELETE", $"/api/products/{EmptyGuid}", false),
        ("POST", "/api/categories", true),
        ("PUT", $"/api/categories/{EmptyGuid}", true),
        ("DELETE", $"/api/categories/{EmptyGuid}", false),
        ("POST", "/api/discounts", true),
        ("PUT", "/api/discounts/WELCOME10", true),
        ("DELETE", "/api/discounts/WELCOME10", false),
        ("POST", "/api/shipping-methods", true),
        ("PUT", "/api/shipping-methods/standard", true),
        ("DELETE", "/api/shipping-methods/standard", false),
        ("POST", "/api/tax-categories", true),
        ("POST", "/api/tax-zones", true),
        ("PUT", "/api/tax-zones/us", true),
        ("DELETE", "/api/tax-zones/us", false),
        ("POST", "/api/gift-cards", true),
        ("GET", "/api/gift-cards", false),
        ("POST", "/api/gift-cards/GC-NOPE/deactivate", false),
        ("POST", "/api/orders/ORD-NOPE/fulfill", false),
        ("POST", "/api/orders/ORD-NOPE/fulfillments", true),
        ("GET", "/api/returns", false),
        ("POST", "/api/returns/RMA-NOPE/approve", false),
        ("POST", "/api/returns/RMA-NOPE/reject", true),
        ("GET", "/api/webhooks", false),
        ("POST", "/api/webhooks", true),
        ("PUT", $"/api/webhooks/{EmptyGuid}", true),
        ("DELETE", $"/api/webhooks/{EmptyGuid}", false),
        ("GET", $"/api/webhooks/{EmptyGuid}/deliveries", false),
        ("POST", $"/api/webhooks/deliveries/{EmptyGuid}/retry", false),
        ("GET", "/api/admin/reports/sales", false),
        ("GET", "/api/admin/reports/top-products", false),
        ("GET", "/api/admin/reports/low-stock", false),
        ("GET", "/api/admin/reports/discount-usage", false),
        ("GET", "/api/reviews", false),
        ("POST", $"/api/reviews/{EmptyGuid}/approve", false),
        ("POST", $"/api/reviews/{EmptyGuid}/reject", true),
    ];

    [Fact]
    public async Task AdminEndpoints_Anonymous_AllReturn401()
    {
        foreach (var (method, path, hasBody) in AdminEndpoints)
        {
            var response = await Send(_client, method, path, hasBody);
            Assert.True(HttpStatusCode.Unauthorized == response.StatusCode,
                $"{method} {path} returned {(int)response.StatusCode}, expected 401");
        }
    }

    [Fact]
    public async Task AdminEndpoints_AsCustomer_AllReturn403()
    {
        var customer = _factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, "authz-nobody@example.com"));

        foreach (var (method, path, hasBody) in AdminEndpoints)
        {
            var response = await Send(customer, method, path, hasBody);
            Assert.True(HttpStatusCode.Forbidden == response.StatusCode,
                $"{method} {path} returned {(int)response.StatusCode}, expected 403");
        }
    }

    [Fact]
    public async Task Address_OfAnotherCustomer_IsInvisibleToUpdateDeleteAndDefault()
    {
        var owner = _factory.CreateClient();
        owner.UseBearer(await TestAuth.RegisterAsync(owner, "addr-owner@example.com"));
        var created = await owner.PostAsJsonAsync("/api/me/addresses", new SaveAddressRequest(
            "Home", new AddressDto("Own Er", "1 Mine St", null, "Mytown", "MT", "11111", "US"),
            null));
        created.EnsureSuccessStatusCode();
        var addressId = (await created.Content.ReadFromJsonAsync<CustomerAddressResponse>())!.Id;

        var stranger = _factory.CreateClient();
        stranger.UseBearer(await TestAuth.RegisterAsync(stranger, "addr-stranger@example.com"));

        var update = await stranger.PutAsJsonAsync($"/api/me/addresses/{addressId}",
            new SaveAddressRequest("Stolen",
                new AddressDto("Th Ief", "2 Yours St", null, "Elsewhere", "EW", "22222", "US"),
                null));
        var makeDefault = await stranger.PostAsync($"/api/me/addresses/{addressId}/default", null);
        var delete = await stranger.DeleteAsync($"/api/me/addresses/{addressId}");

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, makeDefault.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        // The owner's address is untouched by all three attempts.
        var mine = await owner.GetFromJsonAsync<List<CustomerAddressResponse>>("/api/me/addresses");
        var address = Assert.Single(mine!);
        Assert.Equal("Home", address.Label);
    }

    [Fact]
    public async Task Wishlist_OfAnotherCustomer_IsInvisibleToReadsAndWrites()
    {
        var owner = _factory.CreateClient();
        owner.UseBearer(await TestAuth.RegisterAsync(owner, "wish-owner@example.com"));
        var wishlist = await owner.GetFromJsonAsync<WishlistResponse>("/api/me/wishlists/default");

        var stranger = _factory.CreateClient();
        stranger.UseBearer(await TestAuth.RegisterAsync(stranger, "wish-stranger@example.com"));
        var variantId = (await _client.GetFromJsonAsync<InventoryResponse>(
            "/api/inventory/CAP-KHK"))!.ProductVariantId;

        var read = await stranger.GetAsync($"/api/me/wishlists/{wishlist!.Id}");
        var addItem = await stranger.PostAsJsonAsync($"/api/me/wishlists/{wishlist.Id}/items",
            new AddWishlistItemRequest(variantId));
        var rename = await stranger.PutAsJsonAsync($"/api/me/wishlists/{wishlist.Id}",
            new CreateWishlistRequest("Hijacked"));
        var delete = await stranger.DeleteAsync($"/api/me/wishlists/{wishlist.Id}");

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, addItem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, string method, string path, bool hasBody)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (hasBody)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }
}
