using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class RecentlyViewedApiTests
{
    [Fact]
    public async Task Explicit_A_B_A_views_upsert_order_and_reads_never_record_views_or_expose_other_history()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "recent");
        using var other = await AccountTestHelpers.Create(scenario, "recent-other");
        Guid[] ids = [];
        await scenario.Db(async db => ids = await db.Products.Where(p => p.IsActive).OrderBy(p => p.Id).Take(2).Select(p => p.Id).ToArrayAsync());
        const string path = "/api/me/recent-products";
        Assert.Empty((await owner.Client.GetFromJsonAsync<List<RecentProductResponse>>(path))!);
        (await owner.Client.GetAsync($"/api/products/{ids[0]}")).EnsureSuccessStatusCode();
        await scenario.Db(async db => Assert.Empty(await db.RecentlyViewedProducts.ToListAsync()));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PostAsync(path + "/" + ids[0], null)).StatusCode);
        scenario.Clock.Instant = scenario.Clock.Instant.AddSeconds(1);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PostAsync(path + "/" + ids[1], null)).StatusCode);
        scenario.Clock.Instant = scenario.Clock.Instant.AddSeconds(1);
        var lastView = scenario.Clock.Instant;
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PostAsync(path + "/" + ids[0], null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.Client.PostAsync(path + "/" + ids[1], null)).StatusCode);
        scenario.Clock.Instant = scenario.Clock.Instant.AddSeconds(1);
        scenario.Commands.Statements.Clear();
        var response = await owner.Client.GetAsync(path); response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.NoStore);
        var recent = (await response.Content.ReadFromJsonAsync<List<RecentProductResponse>>())!;
        Assert.Equal(ids, recent.Select(r => r.Product.Id)); Assert.Equal(lastView, recent[0].LastViewedAt);
        Assert.All(scenario.Commands.Statements, sql => { Assert.DoesNotContain("INSERT INTO", sql); Assert.DoesNotContain("UPDATE ", sql); Assert.DoesNotContain("DELETE FROM", sql); });
        Assert.Equal(ids[1], Assert.Single((await other.Client.GetFromJsonAsync<List<RecentProductResponse>>(path))!).Product.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync(path)).StatusCode);
        await scenario.Db(async db =>
        {
            Assert.False(await db.RecentlyViewedProducts.AnyAsync(r => r.CustomerId == owner.Id));
            Assert.Equal(1, await db.RecentlyViewedProducts.CountAsync(r => r.CustomerId == other.Id));
        });
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PostAsync(path + "/" + Guid.NewGuid(), null)).StatusCode);
        await scenario.Db(async db => { (await db.Products.SingleAsync(p => p.Id == ids[0])).IsActive = false; await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PostAsync(path + "/" + ids[0], null)).StatusCode);
        using var visitor = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PostAsync(path + "/" + ids[1], null)).StatusCode);
    }

    [Fact]
    public async Task Retention_keeps_fifty_and_active_filter_precedes_twenty_item_limit_with_stable_ties()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "recent-cap");
        var origin = scenario.Clock.Instant.AddDays(-1);
        Product[] products = [];
        await scenario.Db(async db =>
        {
            var category = (await db.Categories.FirstAsync()).Id;
            products = Enumerable.Range(0, 51).Select(i => new Product { CategoryId = category, Name = $"Recent {i}", Slug = "recent-" + Guid.NewGuid().ToString("N") }).ToArray();
            db.Products.AddRange(products);
            db.RecentlyViewedProducts.AddRange(products.Take(49).Select((p, i) => new RecentlyViewedProduct(owner.Id, p.Id, origin.AddSeconds(i))));
            await db.SaveChangesAsync();
        });
        const string path = "/api/me/recent-products";
        scenario.Clock.Instant = origin.AddSeconds(49);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PostAsync(path + "/" + products[49].Id, null)).StatusCode);
        scenario.Clock.Instant = origin.AddSeconds(50);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PostAsync(path + "/" + products[50].Id, null)).StatusCode);
        await scenario.Db(async db =>
        {
            Assert.Equal(50, await db.RecentlyViewedProducts.CountAsync(r => r.CustomerId == owner.Id));
            Assert.False(await db.RecentlyViewedProducts.AnyAsync(r => r.CustomerId == owner.Id && r.ProductId == products[0].Id));
            var newest = products.Skip(46).Select(p => p.Id).ToArray();
            foreach (var product in await db.Products.Where(p => newest.Contains(p.Id)).ToListAsync()) product.IsActive = false;
            await db.SaveChangesAsync();
        });
        var recent = (await owner.Client.GetFromJsonAsync<List<RecentProductResponse>>(path))!;
        Assert.Equal(20, recent.Count);
        Assert.Equal(products.Skip(26).Take(20).Reverse().Select(p => p.Id), recent.Select(r => r.Product.Id));
        // Same instant: documented product-ID ordering decides the tie, without sleeps.
        scenario.Clock.Instant = origin.AddMinutes(2);
        await owner.Client.PostAsync(path + "/" + products[1].Id, null);
        await owner.Client.PostAsync(path + "/" + products[2].Id, null);
        recent = (await owner.Client.GetFromJsonAsync<List<RecentProductResponse>>(path))!;
        Assert.Equal(new[] { products[1].Id, products[2].Id }.OrderBy(id => id.ToString(), StringComparer.Ordinal), recent.Take(2).Select(r => r.Product.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Admin.DeleteAsync($"/api/products/{products[1].Id}")).StatusCode);
        await scenario.Db(async db => Assert.False(await db.RecentlyViewedProducts.AnyAsync(r => r.ProductId == products[1].Id)));
    }
}
