using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class CatalogOrganizationApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _public = factory.CreateClient();
    private static string Key() => Guid.NewGuid().ToString("N");
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private async Task<HttpClient> Admin()
    {
        var client = factory.CreateClient();
        await client.AuthenticateAsAdminAsync();
        return client;
    }
    private async Task<Product[]> Products()
    {
        var products = new List<Product>();
        await factory.WithDbAsync(async db =>
        {
            for (var i = 0; i < 3; i++)
            {
                var category = new Category { Name = Key(), Slug = Key() };
                var product = new Product { CategoryId = category.Id, Name = "Choice " + i, Slug = Key() };
                product.Variants.Add(new ProductVariant { ProductId = product.Id, Sku = Key(), Name = "Variant", Price = new Money(10) });
                db.AddRange(category, product);
                products.Add(product);
            }
            await db.SaveChangesAsync();
        });
        return products.ToArray();
    }

    [Fact]
    public async Task Tags_normalize_assign_filter_before_paging_and_preserve_response_membership()
    {
        var admin = await Admin();
        var products = await Products();
        var slug = "summer-" + Key();
        var tag = await Read<TagResponse>(await admin.PostAsJsonAsync("/api/admin/tags", new { name = " Summer ", slug = " " + slug.ToUpperInvariant() + " " }));
        Assert.Equal("Summer", tag.Name);
        Assert.Equal(slug, tag.Slug);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/tags", new { name = "Duplicate", slug = slug.ToUpperInvariant() })).StatusCode);
        var second = await Read<TagResponse>(await admin.PostAsJsonAsync("/api/admin/tags", new { name = "Another", slug = "aaa-" + Key() }));
        foreach (var product in products.Take(2))
        {
            var assigned = await Read<ProductTagsResponse>(await admin.PutAsJsonAsync($"/api/admin/products/{product.Id}/tags",
                new { tagIds = new[] { tag.Id, second.Id }, expectedVersion = 0 }));
            Assert.Equal(1, assigned.TagVersion);
            Assert.Equal(new[] { second.Slug, tag.Slug }, assigned.Tags.Select(t => t.Slug));
        }
        var page = (await _public.GetFromJsonAsync<PagedResult<ProductResponse>>($"/api/products?tagSlug=%20{slug.ToUpperInvariant()}%20&pageSize=1&sort=name"))!;
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.True(page.HasNextPage);
        Assert.Equal(2, page.Items[0].Tags.Count);
        Assert.Equal(1, page.Items[0].TagVersion);
        var intersection = (await _public.GetFromJsonAsync<PagedResult<ProductResponse>>($"/api/products?tagSlug={slug}&categoryId={products[1].CategoryId}"))!;
        Assert.Equal(products[1].Id, Assert.Single(intersection.Items).Id);
        Assert.Equal(0, (await _public.GetFromJsonAsync<PagedResult<ProductResponse>>($"/api/products?tagSlug=missing-{Key()}"))!.TotalCount);
        foreach (var route in new[] { $"/api/products/{products[0].Id}", $"/api/products/by-slug/{products[0].Slug}" })
            Assert.Equal(2, (await _public.GetFromJsonAsync<ProductResponse>(route))!.Tags.Count);
        var edited = await Read<ProductResponse>(await admin.PutAsJsonAsync($"/api/products/{products[0].Id}", new
        {
            categoryId = products[0].CategoryId, name = "Updated metadata", slug = products[0].Slug,
            description = "Tags stay assigned", isActive = true,
        }));
        Assert.Equal(2, edited.Tags.Count);
        Assert.Equal(1, edited.TagVersion);
        var unchanged = (await _public.GetFromJsonAsync<ProductResponse>($"/api/products/{products[2].Id}"))!;
        Assert.Empty(unchanged.Tags);
        Assert.Equal(0, unchanged.TagVersion);
        Assert.Contains((await _public.GetFromJsonAsync<List<TagResponse>>("/api/tags"))!, t => t.Id == tag.Id);
    }

    [Fact]
    public async Task Tag_replacement_rejects_unknown_stale_and_unauthorized_edits_without_changing_membership()
    {
        var admin = await Admin();
        var product = (await Products())[0];
        var tag = await Read<TagResponse>(await admin.PostAsJsonAsync("/api/admin/tags", new { name = "Tag", slug = Key() }));
        var path = $"/api/admin/products/{product.Id}/tags";
        var good = new { tagIds = new[] { tag.Id }, expectedVersion = 0 };
        Assert.Equal(HttpStatusCode.Unauthorized, (await _public.PutAsJsonAsync(path, good)).StatusCode);
        var customer = factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, Key() + "@tags.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PutAsJsonAsync(path, good)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync("/api/admin/tags", new { name = "No", slug = Key() })).StatusCode);
        await Read<ProductTagsResponse>(await admin.PutAsJsonAsync(path, good));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path, new { tagIds = Array.Empty<Guid>(), expectedVersion = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync(path, new { tagIds = new[] { tag.Id, Guid.NewGuid() }, expectedVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path, new { tagIds = new[] { tag.Id, tag.Id }, expectedVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path, new { tagIds = Array.Empty<Guid>() })).StatusCode);
        Assert.Single((await _public.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}"))!.Tags);
        var cleared = await Read<ProductTagsResponse>(await admin.PutAsJsonAsync(path, new { tagIds = Array.Empty<Guid>(), expectedVersion = 1 }));
        Assert.Empty(cleared.Tags);
        Assert.Equal(2, cleared.TagVersion);
        Assert.Empty((await _public.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}"))!.Tags);
        foreach (var invalid in new[] { "-start", "end-", "two--hyphens", "space here", "café", new string('a', 61) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/tags", new { name = "Invalid", slug = invalid })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _public.GetAsync("/api/products?tagSlug=" + Uri.EscapeDataString(invalid))).StatusCode);
        }
    }

    [Fact]
    public async Task Collections_preserve_editorial_order_filter_inactive_members_and_keep_drafts_private()
    {
        var admin = await Admin();
        var products = await Products();
        var collection = await Read<CollectionAdminResponse>(await admin.PostAsJsonAsync("/api/admin/collections", new { title = " Starter workspace ", slug = Key() }));
        Assert.False(collection.IsPublished);
        Assert.Empty(collection.ProductIds);
        var publicPath = $"/api/collections/{collection.Slug}";
        var adminPath = $"/api/admin/collections/{collection.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await _public.GetAsync(publicPath)).StatusCode);
        collection = await Read<CollectionAdminResponse>(await admin.PutAsJsonAsync(adminPath,
            new { title = "Starter workspace", isPublished = true, productIds = products.Select(p => p.Id).ToArray(), expectedVersion = 0 }));
        var reordered = new[] { products[2].Id, products[0].Id, products[1].Id };
        collection = await Read<CollectionAdminResponse>(await admin.PutAsJsonAsync(adminPath,
            new { title = "Reordered", isPublished = true, productIds = reordered, expectedVersion = collection.Version }));
        Assert.Equal(reordered, (await admin.GetFromJsonAsync<CollectionAdminResponse>(adminPath))!.ProductIds);
        var visible = (await _public.GetFromJsonAsync<PublicCollectionResponse>(publicPath))!;
        Assert.Equal(reordered, visible.Products.Items.Select(p => p.Id));
        await factory.WithDbAsync(async db => { (await db.Products.SingleAsync(p => p.Id == products[0].Id)).IsActive = false; await db.SaveChangesAsync(); });
        visible = (await _public.GetFromJsonAsync<PublicCollectionResponse>(publicPath + "?pageSize=1&page=2"))!;
        Assert.Equal(2, visible.Products.TotalCount);
        Assert.Equal(products[1].Id, Assert.Single(visible.Products.Items).Id);
        Assert.True(visible.Products.HasPreviousPage);
        Assert.False(visible.Products.HasNextPage);
        Assert.Equal(reordered, (await admin.GetFromJsonAsync<CollectionAdminResponse>(adminPath))!.ProductIds);
        var hidden = await Read<CollectionAdminResponse>(await admin.PutAsJsonAsync(adminPath,
            new { title = "Hidden", isPublished = false, productIds = reordered, expectedVersion = collection.Version }));
        Assert.Equal(HttpStatusCode.NotFound, (await _public.GetAsync(publicPath)).StatusCode);
        Assert.Equal(reordered, hidden.ProductIds);
        (await admin.DeleteAsync($"/api/products/{products[0].Id}")).EnsureSuccessStatusCode();
        var afterDelete = (await admin.GetFromJsonAsync<CollectionAdminResponse>(adminPath))!;
        Assert.Equal(new[] { products[2].Id, products[1].Id }, afterDelete.ProductIds);
        Assert.True(afterDelete.Version > hidden.Version);
    }

    [Fact]
    public async Task Collection_replacement_validates_every_member_before_mutation_and_rejects_stale_editors()
    {
        var admin = await Admin();
        var products = await Products();
        var collection = await Read<CollectionAdminResponse>(await admin.PostAsJsonAsync("/api/admin/collections", new { title = "List", slug = Key() }));
        var path = $"/api/admin/collections/{collection.Id}";
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/collections", new { title = "Duplicate", slug = collection.Slug.ToUpperInvariant() })).StatusCode);
        var ids = new[] { products[0].Id };
        await Read<CollectionAdminResponse>(await admin.PutAsJsonAsync(path, new { title = "Published", isPublished = true, productIds = ids, expectedVersion = 0 }));
        foreach (var badIds in new[] { new[] { products[0].Id, products[0].Id }, new[] { products[1].Id, Guid.NewGuid() } })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync(path,
                new { title = "Must not persist", isPublished = false, productIds = badIds, expectedVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path,
            new { title = "Stale", isPublished = false, productIds = Array.Empty<Guid>(), expectedVersion = 0 })).StatusCode);
        var after = (await admin.GetFromJsonAsync<CollectionAdminResponse>(path))!;
        Assert.Equal(ids, after.ProductIds);
        Assert.Equal("Published", after.Title);
        Assert.True(after.IsPublished);
        Assert.Equal(1, after.Version);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _public.GetAsync(path)).StatusCode);
        var customer = factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, Key() + "@collections.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PutAsJsonAsync(path,
            new { title = "Forbidden", isPublished = true, productIds = ids, expectedVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _public.GetAsync($"/api/collections/{collection.Slug}?page=2147483647&pageSize=100")).StatusCode);
    }
}
