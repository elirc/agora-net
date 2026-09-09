using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class AuthApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly AddressDto Address = new(
        "Alan Turing", "1 Bletchley Park", null, "Milton Keynes", "BKM", "MK3 6EB", "GB");

    [Fact]
    public async Task Register_ReturnsTokenAndCustomerProfile()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Alan@Example.com", "S3cure-pass!", "Alan Turing"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.NotEmpty(auth.Token);
        Assert.True(auth.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal("alan@example.com", auth.Customer.Email); // normalized
        Assert.Equal("Customer", auth.Customer.Role);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        await TestAuth.RegisterAsync(_client, "dupe@example.com");

        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("DUPE@example.com", "AnotherPass1!", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("shorty@example.com", "short", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        await TestAuth.RegisterAsync(_client, "login-ok@example.com");

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("login-ok@example.com", TestAuth.CustomerPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotEmpty(auth!.Token);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        await TestAuth.RegisterAsync(_client, "login-bad@example.com");

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("login-bad@example.com", "WrongPassword1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("ghost@example.com", "DoesNotMatter1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithToken_ReturnsProfile()
    {
        var client = _factory.CreateClient();
        var token = await TestAuth.RegisterAsync(client, "me@example.com", fullName: "Me Myself");
        client.UseBearer(token);

        var me = await client.GetFromJsonAsync<CustomerResponse>("/api/auth/me");

        Assert.Equal("me@example.com", me!.Email);
        Assert.Equal("Me Myself", me.FullName);
    }

    [Fact]
    public async Task Me_Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminMutations_Anonymous_Return401()
    {
        var category = await _client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest("Nope", null, null, null));
        var stock = await _client.PutAsJsonAsync("/api/inventory/TEE-BLK-S", new SetStockRequest(1));
        var discount = await _client.PostAsJsonAsync("/api/discounts",
            new CreateDiscountRequest("NOPE", "Percentage", 5m, null, null, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, category.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stock.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, discount.StatusCode);
    }

    [Fact]
    public async Task AdminMutations_AsCustomer_Return403()
    {
        var client = _factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, "plain-customer@example.com"));

        var category = await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest("Nope", null, null, null));
        var stock = await client.PutAsJsonAsync("/api/inventory/TEE-BLK-S", new SetStockRequest(1));
        var product = await client.DeleteAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, category.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, stock.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, product.StatusCode);
    }

    [Fact]
    public async Task AdminMutations_AsAdmin_Succeed()
    {
        var client = _factory.CreateClient();
        await client.AuthenticateAsAdminAsync();

        var response = await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest("Admin Made", null, null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CatalogReads_RemainPublic()
    {
        var products = await _client.GetAsync("/api/products");
        var categories = await _client.GetAsync("/api/categories");
        var inventory = await _client.GetAsync("/api/inventory/TEE-BLK-S");

        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
        Assert.Equal(HttpStatusCode.OK, categories.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inventory.StatusCode);
    }

    [Fact]
    public async Task Cart_CreatedWhileSignedIn_IsAttachedToCustomer()
    {
        var client = _factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, "cart-owner@example.com"));

        var response = await client.PostAsync("/api/carts", null);
        var cart = await response.Content.ReadFromJsonAsync<CartResponse>();

        await _factory.WithDbAsync(async db =>
        {
            var entity = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstAsync(db.Carts, c => c.Token == cart!.Token);
            Assert.NotNull(entity.CustomerId);
        });
    }

    [Fact]
    public async Task GuestCart_Claim_AttachesToAccount()
    {
        var guestCartResponse = await _client.PostAsync("/api/carts", null);
        var token = (await guestCartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var client = _factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, "claimer@example.com"));
        var claim = await client.PostAsync($"/api/carts/{token}/claim", null);

        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var entity = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstAsync(db.Carts, c => c.Token == token);
            Assert.NotNull(entity.CustomerId);
        });
    }

    [Fact]
    public async Task Claim_CartOwnedByAnotherCustomer_Returns409()
    {
        var first = _factory.CreateClient();
        first.UseBearer(await TestAuth.RegisterAsync(first, "owner-a@example.com"));
        var cartResponse = await first.PostAsync("/api/carts", null);
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var second = _factory.CreateClient();
        second.UseBearer(await TestAuth.RegisterAsync(second, "owner-b@example.com"));
        var claim = await second.PostAsync($"/api/carts/{token}/claim", null);

        Assert.Equal(HttpStatusCode.Conflict, claim.StatusCode);
    }

    [Fact]
    public async Task OrderHistory_ShowsSignedInCheckouts_NewestFirst()
    {
        var client = _factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, "history@example.com"));

        var firstNumber = await CheckoutAs(client, "TEE-BLK-M", 1);
        var secondNumber = await CheckoutAs(client, "CAP-KHK", 1);

        var history = await client.GetFromJsonAsync<PagedResult<OrderResponse>>("/api/me/orders");

        Assert.Equal(2, history!.TotalCount);
        Assert.Equal(secondNumber, history.Items[0].Number);
        Assert.Equal(firstNumber, history.Items[1].Number);
    }

    [Fact]
    public async Task OrderHistory_DoesNotIncludeOtherCustomersOrGuestOrders()
    {
        // A guest places an order...
        await CheckoutAs(_client, "CDL-CDR-S", 1);

        // ...and a signed-in customer sees an empty history.
        var client = _factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, "empty-history@example.com"));
        var history = await client.GetFromJsonAsync<PagedResult<OrderResponse>>("/api/me/orders");

        Assert.Equal(0, history!.TotalCount);
        Assert.Empty(history.Items);
    }

    [Fact]
    public async Task OrderHistory_Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/api/me/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GuestCheckout_StillWorks_WithoutAccount()
    {
        var number = await CheckoutAs(_client, "KET-EMB-1L", 1);

        var order = await _client.GetFromJsonAsync<OrderResponse>($"/api/orders/{number}");
        Assert.Equal("Paid", order!.Status);
    }

    private async Task<string> CheckoutAs(HttpClient client, string sku, int quantity)
    {
        var cartResponse = await client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var inventory = await client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        var add = await client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, quantity));
        add.EnsureSuccessStatusCode();

        var checkout = await client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "shopper@example.com", Address, null, "tok_visa"));
        checkout.EnsureSuccessStatusCode();
        var receipt = (await checkout.Content.ReadFromJsonAsync<CheckoutResponse>())!;
        if (receipt.GuestOrderAccessToken is not null)
        {
            client.DefaultRequestHeaders.Remove("X-Agora-Order-Access");
            client.DefaultRequestHeaders.Add("X-Agora-Order-Access", receipt.GuestOrderAccessToken);
        }
        return receipt.Number;
    }
}
