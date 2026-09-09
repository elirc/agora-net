using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class SavedSearchesApiTests
{
    [Fact]
    public async Task Saved_results_share_literal_filtering_and_current_catalog_mapping_with_public_search()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "search");
        var category = new Category { Name = "Saved search", Slug = "search-" + Guid.NewGuid().ToString("N") };
        var first = Product(category.Id, "50%_special A", 10);
        await scenario.Db(async db =>
        {
            db.AddRange(category, first, Product(category.Id, "50XXspecial false match", 10), Product(category.Id, "50%_special expensive", 18));
            await db.SaveChangesAsync();
        });
        var definition = new SavedSearchDefinition("50%_special", category.Id, null, 9, 15, "usd", true, true, "name");
        var created = await owner.Client.PostAsJsonAsync("/api/me/saved-searches", new CreateSavedSearchRequest("  Affordable specials  ", definition));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = (await created.Content.ReadFromJsonAsync<SavedSearchResponse>())!;
        Assert.Equal("Affordable specials", saved.Name); Assert.Equal(1, saved.SchemaVersion); Assert.True(saved.CanRun);
        Assert.Equal(definition, saved.Definition);
        var publicPath = $"/api/products?search=50%25_special&categoryId={category.Id}&minPrice=9&maxPrice=15&currency=usd&inStock=true&isActive=true&sort=name";
        var savedPath = $"/api/me/saved-searches/{saved.Id}/results";
        var publicResult = (await owner.Client.GetFromJsonAsync<PagedResult<ProductResponse>>(publicPath))!;
        var savedResult = (await owner.Client.GetFromJsonAsync<PagedResult<ProductResponse>>(savedPath))!;
        Assert.Equal(first.Id, Assert.Single(savedResult.Items).Id);
        Assert.Equal(publicResult.Items.Select(p => p.Id), savedResult.Items.Select(p => p.Id));
        var later = Product(category.Id, "50%_special C", 12);
        await scenario.Db(async db => { db.Products.Add(later); await db.SaveChangesAsync(); });
        savedResult = (await owner.Client.GetFromJsonAsync<PagedResult<ProductResponse>>(savedPath + "?pageSize=1&page=2"))!;
        Assert.Equal(2, savedResult.TotalCount); Assert.Equal(later.Id, Assert.Single(savedResult.Items).Id);
        publicResult = (await owner.Client.GetFromJsonAsync<PagedResult<ProductResponse>>(publicPath + "&pageSize=1&page=2"))!;
        Assert.Equal(publicResult.Items.Select(p => p.Id), savedResult.Items.Select(p => p.Id));
        await scenario.Db(async db =>
        {
            var stored = await db.SavedCatalogSearches.SingleAsync(s => s.Id == saved.Id);
            Assert.DoesNotContain("page", stored.DefinitionJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tagSlug", stored.DefinitionJson);
            Assert.DoesNotContain("sku", stored.DefinitionJson);
        });
    }

    [Fact]
    public async Task Whitelist_validation_unknown_versions_removed_categories_and_ownership_are_explicit()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "search-owner");
        using var other = await AccountTestHelpers.Create(scenario, "search-other");
        var category = new Category { Name = "Disposable", Slug = "removed-" + Guid.NewGuid().ToString("N") };
        await scenario.Db(async db => { db.Categories.Add(category); await db.SaveChangesAsync(); });
        var created = await owner.Client.PostAsJsonAsync("/api/me/saved-searches", new CreateSavedSearchRequest("Removed category", new(CategoryId: category.Id)));
        created.EnsureSuccessStatusCode(); var saved = (await created.Content.ReadFromJsonAsync<SavedSearchResponse>())!;
        var path = $"/api/me/saved-searches/{saved.Id}";
        await scenario.Db(async db => { db.Categories.Remove(await db.Categories.SingleAsync(c => c.Id == category.Id)); await db.SaveChangesAsync(); });
        Assert.Empty((await owner.Client.GetFromJsonAsync<PagedResult<ProductResponse>>(path + "/results"))!.Items);
        foreach (var suffix in new[] { "", "/results" }) Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(path + suffix)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.DeleteAsync(path)).StatusCode);
        Assert.Empty((await other.Client.GetFromJsonAsync<List<SavedSearchResponse>>("/api/me/saved-searches"))!);
        foreach (var definition in new[] { new SavedSearchDefinition(MinPrice: 1.001m), new(MinPrice: 10, MaxPrice: 5), new(Currency: "US"), new(Search: new string('s', 201)), new(Sort: new string('s', 9000)) })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync("/api/me/saved-searches", new CreateSavedSearchRequest("Invalid", definition))).StatusCode);
        foreach (var unknown in new[] { "page", "rawSql", "sku", "tagSlug" })
        {
            using var content = new StringContent("{\"name\":\"Unknown\",\"definition\":{\"" + unknown + "\":\"bad\"}}", System.Text.Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsync("/api/me/saved-searches", content)).StatusCode);
        }
        await scenario.Db(async db =>
        {
            var stored = await db.SavedCatalogSearches.SingleAsync(s => s.Id == saved.Id);
            db.Entry(stored).Property(s => s.SchemaVersion).CurrentValue = 2; await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.GetAsync(path + "/results")).StatusCode);
        var unsupported = (await owner.Client.GetFromJsonAsync<SavedSearchResponse>(path))!;
        Assert.False(unsupported.CanRun); Assert.Null(unsupported.Definition); Assert.Contains("version 2", unsupported.UnavailableReason!);
        await scenario.Db(async db =>
        {
            var stored = await db.SavedCatalogSearches.SingleAsync(s => s.Id == saved.Id);
            db.Entry(stored).Property(s => s.SchemaVersion).CurrentValue = 1;
            db.Entry(stored).Property(s => s.DefinitionJson).CurrentValue = "{\"minPrice\":0.001}";
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.GetAsync(path + "/results")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync(path)).StatusCode);
        using var visitor = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/me/saved-searches")).StatusCode);
    }

    [Fact]
    public async Task Fifty_search_cap_is_per_account_and_deletion_frees_a_slot()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "search-cap");
        using var other = await AccountTestHelpers.Create(scenario, "search-cap-other");
        await scenario.Db(async db =>
        {
            db.SavedCatalogSearches.AddRange(Enumerable.Range(0, 49).Select(i => new SavedCatalogSearch(owner.Id, $"Search {i}", "{}", scenario.Clock.Instant)));
            await db.SaveChangesAsync();
        });
        var request = new CreateSavedSearchRequest("Last slot", new());
        var last = await owner.Client.PostAsJsonAsync("/api/me/saved-searches", request); Assert.Equal(HttpStatusCode.Created, last.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync("/api/me/saved-searches", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await other.Client.PostAsJsonAsync("/api/me/saved-searches", request)).StatusCode);
        var id = (await last.Content.ReadFromJsonAsync<SavedSearchResponse>())!.Id;
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/me/saved-searches/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.Client.PostAsJsonAsync("/api/me/saved-searches", request)).StatusCode);
        Assert.Equal(50, (await owner.Client.GetFromJsonAsync<List<SavedSearchResponse>>("/api/me/saved-searches"))!.Count);
    }

    private static Product Product(Guid category, string name, decimal price)
    {
        var product = new Product { CategoryId = category, Name = name, Slug = Guid.NewGuid().ToString("N") };
        var variant = new ProductVariant { ProductId = product.Id, Name = "Standard", Sku = Guid.NewGuid().ToString("N"), Price = new Money(price) };
        variant.Inventory = new InventoryItem(variant.Id, 10); product.Variants.Add(variant); return product;
    }
}
