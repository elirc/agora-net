using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class WishlistEditingApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private async Task<HttpClient> Customer()
    {
        var client = factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, Guid.NewGuid().ToString("N") + "@wishlist-edit.test"));
        return client;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<WishlistResponse> Create(HttpClient client) => await Read<WishlistResponse>(
        await client.PostAsJsonAsync("/api/me/wishlists", new { name = Guid.NewGuid().ToString("N") }));

    private static async Task<WishlistResponse> Add(HttpClient client, Guid list, string sku)
    {
        var stock = await client.GetFromJsonAsync<InventoryResponse>("/api/inventory/" + sku);
        return await Read<WishlistResponse>(await client.PostAsJsonAsync($"/api/me/wishlists/{list}/items", new { productVariantId = stock!.ProductVariantId }));
    }

    [Fact]
    public async Task Notes_are_private_versioned_trimmed_and_independent_of_stock_and_membership()
    {
        var owner = await Customer();
        var list = await Create(owner);
        list = await Add(owner, list.Id, "CAP-KHK");
        var item = Assert.Single(list.Items);
        Assert.Null(item.Note);
        Assert.Equal(0, item.NoteVersion);
        var path = $"/api/me/wishlists/{list.Id}/items/{item.Id}/note";
        var saved = await Read<WishlistNoteResponse>(await owner.PutAsJsonAsync(path, new { note = "  gift for Sam  ", expectedVersion = 0 }));
        Assert.Equal("gift for Sam", saved.Note);
        Assert.Equal(1, saved.NoteVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync(path, new { note = "stale", expectedVersion = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(path, new { note = "missing version" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(path, new { note = new string('x', 501), expectedVersion = 1 })).StatusCode);
        var maximum = await Read<WishlistNoteResponse>(await owner.PutAsJsonAsync(path, new { note = " " + new string('x', 500) + " ", expectedVersion = 1 }));
        Assert.Equal(500, maximum.Note!.Length);
        var literal = await Read<WishlistNoteResponse>(await owner.PutAsJsonAsync(path, new { note = "<b>gift</b>", expectedVersion = 2 }));
        Assert.Equal("<b>gift</b>", literal.Note);
        var cleared = await Read<WishlistNoteResponse>(await owner.PutAsJsonAsync(path, new { note = " \t ", expectedVersion = 3 }));
        Assert.Null(cleared.Note);
        var current = (await owner.GetFromJsonAsync<WishlistResponse>($"/api/me/wishlists/{list.Id}"))!;
        Assert.Equal(list.MembershipVersion, current.MembershipVersion);
        Assert.Equal(item.InStock, current.Items[0].InStock);
        Assert.Equal(item.BackInStock, current.Items[0].BackInStock);
        Assert.Null(current.Items[0].Note);
        Assert.Equal(4, current.Items[0].NoteVersion);
        var stranger = await Customer();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync(path, new { note = "foreign", expectedVersion = 4 })).StatusCode);
        var other = await Create(owner);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync($"/api/me/wishlists/{other.Id}/items/{item.Id}/note", new { note = "wrong parent", expectedVersion = 4 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PutAsJsonAsync(path, new { note = "anonymous", expectedVersion = 4 })).StatusCode);
        await factory.WithDbAsync(async db =>
        {
            var stored = await db.WishlistItems.SingleAsync(i => i.Id == item.Id);
            Assert.Null(stored.Note);
            Assert.Equal(4, stored.NoteVersion);
        });
    }

    [Fact]
    public async Task Copy_preserves_source_skips_overlap_and_does_not_copy_notes()
    {
        var owner = await Customer();
        var source = await Create(owner);
        source = await Add(owner, source.Id, "CAP-KHK");
        source = await Add(owner, source.Id, "TEE-BLK-M");
        var target = await Create(owner);
        target = await Add(owner, target.Id, "TEE-BLK-M");
        var first = source.Items.Single(i => i.Sku == "CAP-KHK");
        var overlap = source.Items.Single(i => i.Sku == "TEE-BLK-M");
        await Read<WishlistNoteResponse>(await owner.PutAsJsonAsync($"/api/me/wishlists/{source.Id}/items/{first.Id}/note", new { note = "private recipient", expectedVersion = 0 }));
        var request = new { sourceId = source.Id, itemIds = new[] { first.Id, overlap.Id }, expectedTargetVersion = target.MembershipVersion };
        var path = $"/api/me/wishlists/{target.Id}/copy-items";
        var copied = await Read<WishlistCopyResponse>(await owner.PostAsJsonAsync(path, request));
        Assert.Equal(new[] { first.ProductVariantId }, copied.AddedVariantIds);
        Assert.Equal(new[] { overlap.ProductVariantId }, copied.SkippedVariantIds);
        Assert.True(copied.MembershipVersion > target.MembershipVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(path, request)).StatusCode);
        var repeat = await Read<WishlistCopyResponse>(await owner.PostAsJsonAsync(path,
            new { sourceId = source.Id, itemIds = request.itemIds, expectedTargetVersion = copied.MembershipVersion }));
        Assert.Empty(repeat.AddedVariantIds);
        Assert.Equal(new[] { first.ProductVariantId, overlap.ProductVariantId }, repeat.SkippedVariantIds);
        Assert.Equal(copied.MembershipVersion, repeat.MembershipVersion);
        var targetAfter = (await owner.GetFromJsonAsync<WishlistResponse>($"/api/me/wishlists/{target.Id}"))!;
        var sourceAfter = (await owner.GetFromJsonAsync<WishlistResponse>($"/api/me/wishlists/{source.Id}"))!;
        Assert.Equal(source.Items.Select(i => i.Id), sourceAfter.Items.Select(i => i.Id));
        Assert.Equal(source.MembershipVersion, sourceAfter.MembershipVersion);
        Assert.Equal("private recipient", sourceAfter.Items.Single(i => i.Id == first.Id).Note);
        var newItem = targetAfter.Items.Single(i => i.ProductVariantId == first.ProductVariantId);
        Assert.NotEqual(first.Id, newItem.Id);
        Assert.Null(newItem.Note);
        Assert.Equal(0, newItem.NoteVersion);
    }

    [Fact]
    public async Task Copy_validates_entire_owned_source_set_before_mutation()
    {
        var owner = await Customer();
        var source = await Create(owner);
        source = await Add(owner, source.Id, "CAP-KHK");
        var target = await Create(owner);
        var item = Assert.Single(source.Items);
        var path = $"/api/me/wishlists/{target.Id}/copy-items";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsJsonAsync(path,
            new { sourceId = source.Id, itemIds = new[] { item.Id, Guid.NewGuid() }, expectedTargetVersion = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsJsonAsync(path,
            new { sourceId = target.Id, itemIds = new[] { item.Id }, expectedTargetVersion = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path,
            new { sourceId = source.Id, itemIds = new[] { item.Id, item.Id }, expectedTargetVersion = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path,
            new { sourceId = source.Id, itemIds = new[] { item.Id } })).StatusCode);
        var stranger = await Customer();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(path,
            new { sourceId = source.Id, itemIds = new[] { item.Id }, expectedTargetVersion = 0 })).StatusCode);
        var foreignSource = await Create(stranger);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync(path,
            new { sourceId = foreignSource.Id, itemIds = new[] { item.Id }, expectedTargetVersion = 0 })).StatusCode);
        var after = (await owner.GetFromJsonAsync<WishlistResponse>($"/api/me/wishlists/{target.Id}"))!;
        Assert.Empty(after.Items);
        Assert.Equal(0, after.MembershipVersion);
        Assert.Single((await owner.GetFromJsonAsync<WishlistResponse>($"/api/me/wishlists/{source.Id}"))!.Items);
    }

    [Fact]
    public async Task Copy_accepts_unavailable_choices_uses_fresh_observations_and_tracks_cascading_deletion()
    {
        var category = new Category { Name = "Private test", Slug = Guid.NewGuid().ToString("N") };
        var product = new Product { CategoryId = category.Id, Name = "Test choice", Slug = Guid.NewGuid().ToString("N") };
        var variant = new ProductVariant { ProductId = product.Id, Sku = Guid.NewGuid().ToString("N"), Name = "Choice", Price = new Money(10) };
        variant.Inventory = new InventoryItem(variant.Id, 0);
        product.Variants.Add(variant);
        await factory.WithDbAsync(async db => { db.AddRange(category, product); await db.SaveChangesAsync(); });
        var owner = await Customer();
        var source = await Create(owner);
        source = await Add(owner, source.Id, variant.Sku);
        var target = await Create(owner);
        var selected = new[] { source.Items[0].Id };
        await Read<WishlistCopyResponse>(await owner.PostAsJsonAsync($"/api/me/wishlists/{target.Id}/copy-items",
            new { sourceId = source.Id, itemIds = selected, expectedTargetVersion = 0 }));
        Assert.False((await owner.GetFromJsonAsync<WishlistResponse>($"/api/me/wishlists/{target.Id}"))!.Items[0].InStock);
        var admin = factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        (await admin.PutAsJsonAsync($"/api/inventory/{variant.Sku}", new SetStockRequest(10))).EnsureSuccessStatusCode();
        var freshTarget = await Create(owner);
        await Read<WishlistCopyResponse>(await owner.PostAsJsonAsync($"/api/me/wishlists/{freshTarget.Id}/copy-items",
            new { sourceId = source.Id, itemIds = selected, expectedTargetVersion = 0 }));
        var fresh = (await owner.GetFromJsonAsync<WishlistResponse>($"/api/me/wishlists/{freshTarget.Id}"))!;
        Assert.True(fresh.Items[0].InStock);
        Assert.False(fresh.Items[0].BackInStock);
        Assert.True((await owner.GetFromJsonAsync<WishlistResponse>($"/api/me/wishlists/{source.Id}"))!.Items[0].BackInStock);
        (await admin.DeleteAsync($"/api/products/{product.Id}")).EnsureSuccessStatusCode();
        foreach (var listId in new[] { source.Id, target.Id, freshTarget.Id })
        {
            var after = (await owner.GetFromJsonAsync<WishlistResponse>($"/api/me/wishlists/{listId}"))!;
            Assert.Empty(after.Items);
            Assert.Equal(2, after.MembershipVersion);
        }
    }

    [Fact]
    public async Task Clear_remove_and_move_advance_membership_but_read_and_rename_do_not()
    {
        var owner = await Customer();
        var list = await Create(owner);
        list = await Add(owner, list.Id, "CAP-KHK");
        Assert.Equal(1, list.MembershipVersion);
        var renamed = await Read<WishlistResponse>(await owner.PutAsJsonAsync($"/api/me/wishlists/{list.Id}", new { name = Guid.NewGuid().ToString("N") }));
        Assert.Equal(1, renamed.MembershipVersion);
        var removed = await Read<WishlistResponse>(await owner.DeleteAsync($"/api/me/wishlists/{list.Id}/items/{list.Items[0].Id}"));
        Assert.Equal(2, removed.MembershipVersion);
        list = await Add(owner, list.Id, "CAP-KHK");
        var cart = await Read<CartResponse>(await owner.PostAsync("/api/carts", null));
        await Read<WishlistNoteResponse>(await owner.PutAsJsonAsync($"/api/me/wishlists/{list.Id}/items/{list.Items[0].Id}/note",
            new { note = "private recipient", expectedVersion = 0 }));
        var movedResponse = await owner.PostAsJsonAsync($"/api/me/wishlists/{list.Id}/items/{list.Items[0].Id}/move-to-cart", new { cartToken = cart.Token });
        movedResponse.EnsureSuccessStatusCode();
        Assert.DoesNotContain("note", await movedResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        list = (await owner.GetFromJsonAsync<WishlistResponse>($"/api/me/wishlists/{list.Id}"))!;
        Assert.Equal(4, list.MembershipVersion);
        list = await Add(owner, list.Id, "CAP-KHK");
        (await owner.DeleteAsync($"/api/me/wishlists/{list.Id}/items")).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/me/wishlists/{list.Id}/items")).EnsureSuccessStatusCode();
        Assert.Equal(6, (await owner.GetFromJsonAsync<WishlistResponse>($"/api/me/wishlists/{list.Id}"))!.MembershipVersion);
    }
}
