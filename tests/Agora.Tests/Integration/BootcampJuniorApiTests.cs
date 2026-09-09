using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

/// <summary>Worked counterexamples for JS-01 through JS-25; fixture data stays isolated by unique IDs.</summary>
public class BootcampJuniorApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static string Key() => Guid.NewGuid().ToString("N");

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpClient> Admin()
    {
        var client = factory.CreateClient();
        await client.AuthenticateAsAdminAsync();
        return client;
    }

    private async Task<(HttpClient Client, Guid Id)> Customer()
    {
        var client = factory.CreateClient();
        client.UseBearer(await TestAuth.RegisterAsync(client, $"{Key()}@bootcamp.test"));
        var profile = await Json(await client.GetAsync("/api/auth/me"));
        return (client, profile.GetProperty("id").GetGuid());
    }

    private async Task<Category> Category(string? name = null, Guid? parent = null)
    {
        var category = new Category { Name = name ?? Key(), Slug = "category-" + Key(), ParentCategoryId = parent };
        await factory.WithDbAsync(async db => { db.Categories.Add(category); await db.SaveChangesAsync(); });
        return category;
    }

    private async Task<Product> Product(Guid categoryId, bool images = false)
    {
        var product = new Product { CategoryId = categoryId, Name = Key(), Slug = Key() };
        var prefix = Key();
        foreach (var (suffix, price, stock) in new[] { ("A", 10m, 0), ("B", 20m, 10) })
        {
            var variant = new ProductVariant
            {
                ProductId = product.Id, Sku = prefix + suffix, Name = suffix,
                Price = new Money(price), WeightGrams = 250,
            };
            variant.Inventory = new InventoryItem(variant.Id, stock);
            product.Variants.Add(variant);
        }
        if (images) product.Images.Add(new ProductImage { ProductId = product.Id, Url = "https://example.test/image" });
        await factory.WithDbAsync(async db => { db.Products.Add(product); await db.SaveChangesAsync(); });
        return product;
    }

    [Fact]
    public async Task Categories_slug_root_children_and_filtered_pages_use_one_scope()
    {
        var prefix = Key();
        var root = await Category(prefix + " root");
        var child = await Category(prefix + " child", root.Id);
        await Category(prefix + " grandchild", child.Id);
        var bySlug = await Json(await _client.GetAsync($"/api/categories/by-slug/%20{root.Slug}%20"));
        Assert.Equal(root.Id, bySlug.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/categories/by-slug/{root.Slug.ToUpperInvariant()}")).StatusCode);
        var roots = await Json(await _client.GetAsync($"/api/categories?search={prefix}&rootOnly=true&pageSize=1"));
        Assert.Equal(1, roots.GetProperty("totalCount").GetInt32());
        Assert.False(roots.GetProperty("hasPreviousPage").GetBoolean());
        Assert.False(roots.GetProperty("hasNextPage").GetBoolean());
        Assert.Equal(root.Id, roots.GetProperty("items")[0].GetProperty("id").GetGuid());
        var children = await Json(await _client.GetAsync($"/api/categories?parentCategoryId={root.Id}"));
        Assert.Equal(child.Id, Assert.Single(children.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var missing = await Json(await _client.GetAsync($"/api/categories?parentCategoryId={Guid.NewGuid()}"));
        Assert.Equal(0, missing.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/categories?rootOnly=true&parentCategoryId={root.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task Category_search_treats_wildcards_literally(string literal)
    {
        var prefix = Key();
        var match = await Category(prefix + literal + "hit");
        await Category(prefix + "Xhit");
        var result = await Json(await _client.GetAsync($"/api/categories?search={Uri.EscapeDataString(prefix + literal)}"));
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(match.Id, result.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Category_pages_are_stable_and_reject_overflow()
    {
        var name = Key();
        var a = await Category(name);
        var b = await Category(name);
        var ids = new[] { a.Id, b.Id }.Order().ToArray();
        for (var page = 1; page <= 2; page++)
        {
            var result = await Json(await _client.GetAsync($"/api/categories?search={name}&pageSize=1&page={page}"));
            Assert.Equal(2, result.GetProperty("totalCount").GetInt32());
            Assert.Equal(ids[page - 1], result.GetProperty("items")[0].GetProperty("id").GetGuid());
        }
        var beyond = await Json(await _client.GetAsync($"/api/categories?search={name}&page=100"));
        Assert.Empty(beyond.GetProperty("items").EnumerateArray());
        Assert.Equal(2, beyond.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/categories?page=2147483647&pageSize=100")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/categories?search=" + new string('a', 201))).StatusCode);
    }

    [Fact]
    public async Task Invalid_parent_update_leaves_every_saved_field_unchanged()
    {
        using var admin = await Admin();
        var category = await Category();
        var response = await admin.PutAsJsonAsync($"/api/categories/{category.Id}",
            new UpdateCategoryRequest("changed", "changed-" + Key(), "changed", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await factory.WithDbAsync(async db =>
        {
            var saved = await db.Categories.SingleAsync(c => c.Id == category.Id);
            Assert.Equal(category.Name, saved.Name);
            Assert.Equal(category.Slug, saved.Slug);
            Assert.Equal(category.Description, saved.Description);
            Assert.Null(saved.ParentCategoryId);
        });
        var parent = await Category();
        var attach = await Json(await admin.PutAsJsonAsync($"/api/categories/{category.Id}",
            new UpdateCategoryRequest(category.Name, category.Slug, category.Description, parent.Id)));
        Assert.Equal(parent.Id, attach.GetProperty("parentCategoryId").GetGuid());
        var root = await Json(await admin.PutAsJsonAsync($"/api/categories/{category.Id}",
            new UpdateCategoryRequest(category.Name, category.Slug, category.Description, null)));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("parentCategoryId").ValueKind);
    }

    [Fact]
    public async Task Sku_price_and_stock_must_match_the_same_variant_and_keep_all_choices()
    {
        var category = await Category();
        var product = await Product(category.Id, images: true);
        await Product(category.Id);
        var soldOut = product.Variants[0].Sku;
        var stocked = product.Variants[1].Sku;
        foreach (var query in new[] { $"sku={soldOut}&inStock=true", $"sku={soldOut}&minPrice=15", $"sku={stocked.ToLowerInvariant()}", $"sku={stocked}suffix" })
        {
            var result = await Json(await _client.GetAsync($"/api/products?categoryId={category.Id}&{query}"));
            Assert.Equal(0, result.GetProperty("totalCount").GetInt32());
        }
        var found = await Json(await _client.GetAsync($"/api/products?sku=%20{stocked}%20&inStock=true&hasImages=true"));
        var item = Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal(2, item.GetProperty("variantCount").GetInt32());
        Assert.Equal(2, item.GetProperty("variants").GetArrayLength());
        Assert.Equal(250, item.GetProperty("variants")[0].GetProperty("weightGrams").GetInt32());
        Assert.Equal(item.GetProperty("images")[0].GetProperty("id").GetGuid(), item.GetProperty("primaryImage").GetProperty("id").GetGuid());
        var noImages = await Json(await _client.GetAsync($"/api/products?categoryId={category.Id}&hasImages=false"));
        Assert.Equal(1, noImages.GetProperty("totalCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, noImages.GetProperty("items")[0].GetProperty("primaryImage").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/products?sku=" + new string('a', 65))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/products?hasImages=maybe")).StatusCode);
    }

    [Fact]
    public async Task Weight_input_roundtrips_and_preserves_omitted_default_on_all_product_routes()
    {
        using var admin = await Admin();
        var category = await Category();
        var slug = Key();
        var response = await admin.PostAsJsonAsync("/api/products", new
        {
            categoryId = category.Id, name = "Weighted", slug,
            variants = new object[]
            {
                new { sku = "Z" + slug, price = 10, weightGrams = 250 },
                new { sku = "A" + slug, price = 20 },
            },
        });
        var created = await Json(response);
        var id = created.GetProperty("id").GetGuid();
        foreach (var product in new[]
        {
            created,
            await Json(await _client.GetAsync($"/api/products/{id}")),
            await Json(await _client.GetAsync($"/api/products/by-slug/{slug}")),
            (await Json(await _client.GetAsync($"/api/products?categoryId={category.Id}"))).GetProperty("items")[0],
        })
        {
            Assert.Equal("A" + slug, product.GetProperty("variants")[0].GetProperty("sku").GetString());
            Assert.Equal(0, product.GetProperty("variants")[0].GetProperty("weightGrams").GetInt32());
            Assert.Equal(250, product.GetProperty("variants")[1].GetProperty("weightGrams").GetInt32());
        }
        foreach (var weight in new[] { -1m, 1000001m, 1.5m })
        {
            var invalid = await admin.PostAsJsonAsync("/api/products", new
            {
                categoryId = category.Id, name = "Invalid", slug = Key(),
                variants = new[] { new { sku = Key(), price = 10, weightGrams = weight } },
            });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        var maximum = await Json(await admin.PostAsJsonAsync("/api/products", new
        {
            categoryId = category.Id, name = "Maximum weight", slug = Key(),
            variants = new[] { new { sku = Key(), price = 10, weightGrams = 1_000_000 } },
        }));
        Assert.Equal(1_000_000, maximum.GetProperty("variants")[0].GetProperty("weightGrams").GetInt32());
    }

    [Fact]
    public async Task Review_filters_preserve_approval_product_scope_and_stable_time_order()
    {
        var category = await Category();
        var product = await Product(category.Id);
        var other = await Product(category.Id);
        var instant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var eligible = new List<Guid>();
        await factory.WithDbAsync(async db =>
        {
            foreach (var (productId, rating, approved, offset) in new[]
            {
                (product.Id, 5, true, 0), (product.Id, 4, true, 0), (product.Id, 4, true, 1),
                (product.Id, 2, true, 2), (product.Id, 5, false, 3), (other.Id, 5, true, 0),
            })
            {
                var customer = new Customer { Email = Key() + "@reviews.test", FullName = "Reviewer" };
                var review = new Review(productId, customer.Id, rating, "Title", "Body") { CreatedAt = instant.AddDays(offset) };
                if (approved) review.Approve(instant);
                if (productId == product.Id && approved && rating >= 4 && offset == 0) eligible.Add(review.Id);
                db.Customers.Add(customer);
                db.Reviews.Add(review);
            }
            await db.SaveChangesAsync();
        });
        var result = await Json(await _client.GetAsync($"/api/products/{product.Id}/reviews?minRating=4&sort=%20OLDEST%20&pageSize=1"));
        Assert.Equal(3, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(eligible.Order().First(), result.GetProperty("items")[0].GetProperty("id").GetGuid());
        var newest = await Json(await _client.GetAsync($"/api/products/{product.Id}/reviews?minRating=4"));
        Assert.Equal(4, newest.GetProperty("items")[0].GetProperty("rating").GetInt32());
        foreach (var query in new[] { "minRating=0", "minRating=6", "minRating=bad", "sort=1", "sort=random" })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/products/{product.Id}/reviews?{query}")).StatusCode);
    }

    [Fact]
    public async Task Shipping_filter_uses_maximum_days_and_rejects_numeric_rate_types_without_writes()
    {
        using var admin = await Admin();
        var fast = new ShippingMethod { Code = "a-" + Key(), Name = "Fast", MinDays = 0, MaxDays = 0, BaseRate = 0 };
        var slow = new ShippingMethod { Code = "z-" + Key(), Name = "Slow", MinDays = 0, MaxDays = 5, BaseRate = 0 };
        var inactive = new ShippingMethod { Code = Key(), Name = "Inactive", MaxDays = 0, IsActive = false };
        await factory.WithDbAsync(async db => { db.ShippingMethods.AddRange(fast, slow, inactive); await db.SaveChangesAsync(); });
        var result = await Json(await _client.GetAsync("/api/shipping-methods?maxDeliveryDays=0"));
        var codes = result.EnumerateArray().Select(x => x.GetProperty("code").GetString()).ToArray();
        Assert.Contains(fast.Code, codes);
        Assert.DoesNotContain(slow.Code, codes);
        Assert.DoesNotContain(inactive.Code, codes);
        foreach (var rate in new[] { "0", "1", "2", "999", "Flat, Weighted", "unknown" })
        {
            var code = Key();
            var create = await admin.PostAsJsonAsync("/api/shipping-methods",
                new CreateShippingMethodRequest(code, "Invalid", rate, 0, 0, null, 0, 1, true, false));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, create.StatusCode);
            var update = await admin.PutAsJsonAsync($"/api/shipping-methods/{fast.Code}",
                new UpdateShippingMethodRequest("Changed", rate, 99, 0, null, 0, 1, true, false));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, update.StatusCode);
            await factory.WithDbAsync(async db =>
            {
                Assert.False(await db.ShippingMethods.AnyAsync(m => m.Code == code));
                var saved = await db.ShippingMethods.SingleAsync(m => m.Id == fast.Id);
                Assert.Equal("Fast", saved.Name);
                Assert.Equal(0, saved.BaseRate);
            });
        }
        var valid = await admin.PutAsJsonAsync($"/api/shipping-methods/{fast.Code}",
            new UpdateShippingMethodRequest("Fast", " flat ", 0, 0, null, 0, 0, true, false));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        foreach (var input in new[] { "-1", "366", "wrong" })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/shipping-methods?maxDeliveryDays={input}")).StatusCode);
    }

    [Fact]
    public async Task Wishlist_search_counts_and_clear_preserve_ownership_and_stock()
    {
        var (a, aId) = await Customer();
        var (b, bId) = await Customer();
        using var owner = a;
        using var other = b;
        var category = await Category();
        var product = await Product(category.Id);
        var prefix = Key();
        var list = new Wishlist { CustomerId = aId, Name = prefix + "% gifts" };
        var foreign = new Wishlist { CustomerId = bId, Name = prefix + "% gifts" };
        var decoy = new Wishlist { CustomerId = aId, Name = prefix + "X gifts" };
        foreach (var variant in product.Variants) list.AddItem(variant.Id, false);
        await factory.WithDbAsync(async db => { db.Wishlists.AddRange(list, foreign, decoy); await db.SaveChangesAsync(); });
        var search = await Json(await owner.GetAsync("/api/me/wishlists?search=" + Uri.EscapeDataString(prefix + "%")));
        Assert.Equal(list.Id, Assert.Single(search.EnumerateArray()).GetProperty("id").GetGuid());
        await factory.WithDbAsync(async db => Assert.True(await db.Wishlists.AnyAsync(w => w.CustomerId == aId && w.IsDefault)));
        var detail = await Json(await owner.GetAsync($"/api/me/wishlists/{list.Id}"));
        Assert.Equal(1, detail.GetProperty("inStockItemCount").GetInt32());
        Assert.Equal(1, detail.GetProperty("outOfStockItemCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/me/wishlists/{list.Id}/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.DeleteAsync($"/api/me/wishlists/{list.Id}/items")).StatusCode);
        for (var repeat = 0; repeat < 2; repeat++)
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/me/wishlists/{list.Id}/items")).StatusCode);
        var empty = await Json(await owner.GetAsync($"/api/me/wishlists/{list.Id}"));
        Assert.Equal(list.Name, empty.GetProperty("name").GetString());
        Assert.Equal(0, empty.GetProperty("inStockItemCount").GetInt32());
        Assert.Equal(0, empty.GetProperty("outOfStockItemCount").GetInt32());
        var defaultList = await Json(await owner.GetAsync("/api/me/wishlists/default"));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/me/wishlists/{defaultList.GetProperty("id").GetGuid()}/items")).StatusCode);
        await factory.WithDbAsync(async db =>
        {
            Assert.True(await db.Wishlists.AnyAsync(w => w.Id == foreign.Id));
            Assert.Equal(10, (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == product.Variants[1].Id)).QuantityOnHand);
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/me/wishlists?search=" + new string('x', 101))).StatusCode);
    }

    [Fact]
    public async Task Address_lookup_and_country_filter_use_customer_identity_without_rewriting_country()
    {
        var (a, aId) = await Customer();
        var (b, bId) = await Customer();
        using var owner = a;
        using var other = b;
        var address = new CustomerAddress { CustomerId = aId, Label = "Home", Address = new Address { Country = "us" }, IsDefault = true };
        var foreign = new CustomerAddress { CustomerId = bId, Label = "Foreign", Address = new Address { Country = "US" } };
        await factory.WithDbAsync(async db =>
        {
            db.CustomerAddresses.AddRange(address, foreign,
                new CustomerAddress { CustomerId = aId, Address = new Address { Country = "CA" } });
            await db.SaveChangesAsync();
        });
        var filtered = await Json(await owner.GetAsync("/api/me/addresses?country=%20US%20"));
        Assert.Equal(address.Id, Assert.Single(filtered.EnumerateArray()).GetProperty("id").GetGuid());
        var detail = await Json(await owner.GetAsync($"/api/me/addresses/{address.Id}"));
        Assert.Equal("us", detail.GetProperty("address").GetProperty("country").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/me/addresses/{address.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/me/addresses/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync($"/api/me/addresses/{address.Id}")).StatusCode);
        using var admin = await Admin();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/me/addresses/{address.Id}")).StatusCode);
        foreach (var invalid in new[] { "USA", "U1", "éé", "ſs" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/me/addresses?country=" + Uri.EscapeDataString(invalid))).StatusCode);
        Assert.Empty((await Json(await owner.GetAsync("/api/me/addresses?country=ZZ"))).EnumerateArray());
    }

    [Fact]
    public async Task Owned_order_status_filter_rejects_numeric_names_and_preserves_counts()
    {
        var (client, customerId) = await Customer();
        using var owner = client;
        var instant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var paid = new Order { Number = Key(), CustomerId = customerId, Email = "owner@test.test", CreatedAt = instant };
        paid.MarkPaid("fake", instant);
        await factory.WithDbAsync(async db =>
        {
            db.Orders.AddRange(paid,
                new Order { Number = Key(), CustomerId = customerId, CreatedAt = instant },
                new Order { Number = Key(), Email = "owner@test.test", CreatedAt = instant });
            await db.SaveChangesAsync();
        });
        var result = await Json(await owner.GetAsync("/api/me/orders?status=%20pAiD%20&pageSize=1"));
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(paid.Number, result.GetProperty("items")[0].GetProperty("number").GetString());
        var all = await Json(await owner.GetAsync("/api/me/orders"));
        Assert.Equal(2, all.GetProperty("totalCount").GetInt32());
        foreach (var invalid in new[] { "1", "999", "Paid,Pending", "anything" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/me/orders?status=" + Uri.EscapeDataString(invalid))).StatusCode);
        await factory.WithDbAsync(async db => Assert.Equal(OrderStatus.Paid, (await db.Orders.SingleAsync(o => o.Id == paid.Id)).Status));
    }

    [Fact]
    public async Task Cart_and_inventory_flags_follow_save_activate_and_stock_updates()
    {
        var category = await Category();
        var product = await Product(category.Id);
        var variant = product.Variants[1];
        var cart = await Json(await _client.PostAsync("/api/carts", null));
        var token = cart.GetProperty("token").GetString();
        var added = await Json(await _client.PostAsJsonAsync($"/api/carts/{token}/items", new AddCartItemRequest(variant.Id, 3)));
        Assert.Equal(1, added.GetProperty("activeLineCount").GetInt32());
        Assert.Equal(3, added.GetProperty("totalQuantity").GetInt32());
        var itemId = added.GetProperty("items")[0].GetProperty("id").GetGuid();
        var saved = await Json(await _client.PostAsync($"/api/carts/{token}/items/{itemId}/save-for-later", null));
        Assert.Equal(0, saved.GetProperty("activeLineCount").GetInt32());
        Assert.Equal(1, saved.GetProperty("savedLineCount").GetInt32());
        Assert.Equal(0, saved.GetProperty("subtotal").GetProperty("amount").GetDecimal());
        var active = await Json(await _client.PostAsync($"/api/carts/{token}/items/{itemId}/activate", null));
        Assert.Equal(1, active.GetProperty("activeLineCount").GetInt32());
        Assert.Equal(0, active.GetProperty("savedLineCount").GetInt32());
        using var admin = await Admin();
        var stock = await Json(await _client.GetAsync($"/api/inventory/{variant.Sku}"));
        Assert.True(stock.GetProperty("inStock").GetBoolean());
        var changed = await Json(await admin.PutAsJsonAsync($"/api/inventory/{variant.Sku}", new SetStockRequest(0)));
        Assert.False(changed.GetProperty("inStock").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/inventory/missing-" + Key())).StatusCode);
    }

    [Fact]
    public async Task Top_product_dates_compare_instants_and_preserve_admin_access()
    {
        using var admin = await Admin();
        var path = "/api/admin/reports/top-products?from=2026-02-02T00:00:00Z&to=2026-02-01T00:00:00Z";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(path)).StatusCode);
        var sameInstant = "/api/admin/reports/top-products?from=" + Uri.EscapeDataString("2026-02-01T01:00:00+01:00")
            + "&to=2026-02-01T00:00:00Z";
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(sameInstant)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/reports/top-products?to=2026-02-01T00:00:00Z")).StatusCode);
    }
}
