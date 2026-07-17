using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

public class WishlistsApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Wishlists_Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/api/me/wishlists");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_AutoCreatesDefaultWishlist()
    {
        var client = await NewCustomer("wl-default@example.com");

        var lists = await client.GetFromJsonAsync<List<WishlistSummaryResponse>>("/api/me/wishlists");

        var only = Assert.Single(lists!);
        Assert.True(only.IsDefault);
        Assert.Equal("Favorites", only.Name);
        Assert.Equal(0, only.ItemCount);
    }

    [Fact]
    public async Task NamedWishlist_CreateRenameDelete()
    {
        var client = await NewCustomer("wl-named@example.com");

        var create = await client.PostAsJsonAsync("/api/me/wishlists",
            new CreateWishlistRequest("Gift ideas"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var wishlist = await create.Content.ReadFromJsonAsync<WishlistResponse>();
        Assert.False(wishlist!.IsDefault);

        var duplicate = await client.PostAsJsonAsync("/api/me/wishlists",
            new CreateWishlistRequest("Gift ideas"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var rename = await client.PutAsJsonAsync($"/api/me/wishlists/{wishlist.Id}",
            new CreateWishlistRequest("Birthday ideas"));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        var delete = await client.DeleteAsync($"/api/me/wishlists/{wishlist.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task DefaultWishlist_CannotBeDeleted()
    {
        var client = await NewCustomer("wl-nodelete@example.com");
        var def = await client.GetFromJsonAsync<WishlistResponse>("/api/me/wishlists/default");

        var delete = await client.DeleteAsync($"/api/me/wishlists/{def!.Id}");

        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task AddItem_DuplicateVariant_Returns409()
    {
        var client = await NewCustomer("wl-dupe@example.com");
        var variantId = await VariantId("CAP-KHK");

        var first = await client.PostAsJsonAsync("/api/me/wishlists/default/items",
            new AddWishlistItemRequest(variantId));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/me/wishlists/default/items",
            new AddWishlistItemRequest(variantId));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task AddItem_UnknownVariant_Returns422()
    {
        var client = await NewCustomer("wl-unknown@example.com");

        var response = await client.PostAsJsonAsync("/api/me/wishlists/default/items",
            new AddWishlistItemRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Wishlists_AreIsolatedBetweenCustomers()
    {
        var alice = await NewCustomer("wl-alice@example.com");
        var created = await (await alice.PostAsJsonAsync("/api/me/wishlists",
            new CreateWishlistRequest("Private"))).Content.ReadFromJsonAsync<WishlistResponse>();

        var bob = await NewCustomer("wl-bob@example.com");
        var get = await bob.GetAsync($"/api/me/wishlists/{created!.Id}");
        var add = await bob.PostAsJsonAsync($"/api/me/wishlists/{created.Id}/items",
            new AddWishlistItemRequest(await VariantId("CAP-KHK")));

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, add.StatusCode);
    }

    [Fact]
    public async Task BackInStock_FlagsAfterRestock()
    {
        // CDL-CDR-L is seeded with zero stock.
        var client = await NewCustomer("wl-restock@example.com");
        var variantId = await VariantId("CDL-CDR-L");

        var added = await client.PostAsJsonAsync("/api/me/wishlists/default/items",
            new AddWishlistItemRequest(variantId));
        var wishlist = await added.Content.ReadFromJsonAsync<WishlistResponse>();
        var item = Assert.Single(wishlist!.Items);
        Assert.False(item.InStock);
        Assert.False(item.BackInStock);

        // Admin restocks; the wishlist item now reads back-in-stock.
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        (await admin.PutAsJsonAsync("/api/inventory/CDL-CDR-L", new SetStockRequest(10)))
            .EnsureSuccessStatusCode();

        var refreshed = await client.GetFromJsonAsync<WishlistResponse>("/api/me/wishlists/default");
        var restocked = Assert.Single(refreshed!.Items);
        Assert.True(restocked.InStock);
        Assert.True(restocked.BackInStock);
    }

    [Fact]
    public async Task ItemInStockAllAlong_DoesNotFlagBackInStock()
    {
        var client = await NewCustomer("wl-instock@example.com");
        var variantId = await VariantId("TEE-BLK-M");

        var added = await client.PostAsJsonAsync("/api/me/wishlists/default/items",
            new AddWishlistItemRequest(variantId));
        var wishlist = await added.Content.ReadFromJsonAsync<WishlistResponse>();

        var item = Assert.Single(wishlist!.Items);
        Assert.True(item.InStock);
        Assert.False(item.BackInStock);
    }

    [Fact]
    public async Task MoveToCart_AddsLine_AndRemovesFromWishlist()
    {
        var client = await NewCustomer("wl-move@example.com");
        var variantId = await VariantId("KET-EMB-1L");
        var added = await client.PostAsJsonAsync("/api/me/wishlists/default/items",
            new AddWishlistItemRequest(variantId));
        var wishlist = await added.Content.ReadFromJsonAsync<WishlistResponse>();
        var item = Assert.Single(wishlist!.Items);

        var cartToken = await NewCartToken(client);
        var move = await client.PostAsJsonAsync(
            $"/api/me/wishlists/{wishlist.Id}/items/{item.Id}/move-to-cart",
            new MoveWishlistItemToCartRequest(cartToken));

        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        var cart = await move.Content.ReadFromJsonAsync<CartResponse>();
        var line = Assert.Single(cart!.Items);
        Assert.Equal("KET-EMB-1L", line.Sku);
        Assert.Equal(1, line.Quantity);

        var refreshed = await client.GetFromJsonAsync<WishlistResponse>("/api/me/wishlists/default");
        Assert.Empty(refreshed!.Items);
    }

    [Fact]
    public async Task MoveToCart_OutOfStock_Returns409_AndKeepsWishlistItem()
    {
        using var localFactory = new AgoraApiFactory();
        var client = localFactory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, "wl-oos@example.com"));

        var inventory = await client.GetFromJsonAsync<InventoryResponse>("/api/inventory/CDL-CDR-L");
        var added = await client.PostAsJsonAsync("/api/me/wishlists/default/items",
            new AddWishlistItemRequest(inventory!.ProductVariantId));
        var wishlist = await added.Content.ReadFromJsonAsync<WishlistResponse>();
        var item = Assert.Single(wishlist!.Items);

        var cartToken = await NewCartToken(client);
        var move = await client.PostAsJsonAsync(
            $"/api/me/wishlists/{wishlist.Id}/items/{item.Id}/move-to-cart",
            new MoveWishlistItemToCartRequest(cartToken));

        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);
        var refreshed = await client.GetFromJsonAsync<WishlistResponse>("/api/me/wishlists/default");
        Assert.Single(refreshed!.Items);
    }

    [Fact]
    public async Task MoveToCart_SomeoneElsesCart_Returns404()
    {
        var owner = await NewCustomer("wl-cart-owner@example.com");
        var ownersCart = await NewCartToken(owner); // attached to owner

        var mover = await NewCustomer("wl-cart-mover@example.com");
        var variantId = await VariantId("CAP-KHK");
        var added = await mover.PostAsJsonAsync("/api/me/wishlists/default/items",
            new AddWishlistItemRequest(variantId));
        var wishlist = await added.Content.ReadFromJsonAsync<WishlistResponse>();
        var item = Assert.Single(wishlist!.Items);

        var move = await mover.PostAsJsonAsync(
            $"/api/me/wishlists/{wishlist.Id}/items/{item.Id}/move-to-cart",
            new MoveWishlistItemToCartRequest(ownersCart));

        Assert.Equal(HttpStatusCode.NotFound, move.StatusCode);
    }

    private async Task<HttpClient> NewCustomer(string email)
    {
        var client = _factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, email));
        return client;
    }

    private async Task<Guid> VariantId(string sku)
    {
        var inventory = await _client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        return inventory!.ProductVariantId;
    }

    private static async Task<string> NewCartToken(HttpClient client)
    {
        var response = await client.PostAsync("/api/carts", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartResponse>())!.Token;
    }
}
