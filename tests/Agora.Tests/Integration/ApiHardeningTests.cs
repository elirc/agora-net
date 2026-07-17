using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

/// <summary>Validation and error-shape hardening across the API surface.</summary>
public class ApiHardeningTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Admin-only mutations are exercised throughout; authenticate up front.
    public Task InitializeAsync() => _client.AuthenticateAsAdminAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _client.GetAsync("/api/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MalformedJson_Returns400()
    {
        var response = await _client.PostAsync("/api/categories",
            new StringContent("{not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ValidationFailure_ReturnsProblemDetailsWithErrors()
    {
        var response = await _client.PostAsJsonAsync("/api/categories", new { slug = "missing-name" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("Name", out _));
    }

    [Fact]
    public async Task DomainRuleViolation_ReturnsProblemDetailsBody()
    {
        var cartResponse = await _client.PostAsync("/api/carts", null);
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var response = await _client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(
            token, "a@b.com",
            new AddressDto("N", "1 St", null, "C", "R", "0", "US"), null, "tok_visa"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Domain rule violation", problem.RootElement.GetProperty("title").GetString());
        Assert.Contains("empty cart", problem.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task CreateVariant_BadCurrency_Returns400()
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");

        var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            categories![0].Id, "Bad Currency", "bad-currency", null, null,
            [new CreateVariantRequest("BADCUR-1", null, 10m, "DOLLARS", null)], null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateVariant_NegativePrice_Returns400()
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");

        var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            categories![0].Id, "Negative Price", "negative-price", null, null,
            [new CreateVariantRequest("NEG-1", null, -1m, null, null)], null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddCartItem_QuantityAboveMax_Returns400()
    {
        var cartResponse = await _client.PostAsync("/api/carts", null);
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;
        var inventory = await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/CDL-CDR-S");

        var response = await _client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, 100));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_MissingPaymentToken_Returns400()
    {
        var cartResponse = await _client.PostAsync("/api/carts", null);
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var response = await _client.PostAsync("/api/checkout", new StringContent(
            JsonSerializer.Serialize(new
            {
                cartToken = token,
                email = "a@b.com",
                shippingAddress = new
                {
                    fullName = "N",
                    line1 = "1 St",
                    city = "C",
                    region = "R",
                    postalCode = "0",
                    country = "US",
                },
            }),
            Encoding.UTF8,
            "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Discount_BadCurrency_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/discounts",
            new CreateDiscountRequest("BADCUR", "FixedAmount", 5m, "US$", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
