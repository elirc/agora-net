using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class CategoryTreeApiTests
{
    [Fact]
    public async Task Moves_use_global_revision_preserve_product_assignments_and_old_routes_cannot_form_cycles()
    {
        using var scenario = await ReportTestScenario.Create(); using var customer = await AccountTestHelpers.Create(scenario, "tree-customer");
        var start = (await scenario.Admin.GetFromJsonAsync<CategoryTreeSnapshot>("/api/admin/category-tree"))!;
        async Task<CategoryResponse> Create(string name, Guid? parent = null)
        {
            var result = await scenario.Admin.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(name, "tree-" + name.ToLowerInvariant(), null, parent));
            Assert.Equal(HttpStatusCode.Created, result.StatusCode); return (await result.Content.ReadFromJsonAsync<CategoryResponse>())!;
        }
        var a = await Create("A"); var b = await Create("B", a.Id); var c = await Create("C", b.Id); var d = await Create("D");
        var snapshot = (await scenario.Admin.GetFromJsonAsync<CategoryTreeSnapshot>("/api/admin/category-tree"))!;
        Assert.Equal(start.Version + 4, snapshot.Version); Assert.True(snapshot.IsValid);
        Assert.Equal(3, snapshot.Nodes.Single(n => n.Id == c.Id).Depth);
        Guid productId = default;
        await scenario.Db(async db => { var product = await db.Products.FirstAsync(); product.CategoryId = c.Id; productId = product.Id; await db.SaveChangesAsync(); });
        var cycle = await scenario.Admin.PostAsJsonAsync($"/api/admin/categories/{a.Id}/move", new MoveCategoryRequest(c.Id, snapshot.Version));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cycle.StatusCode); Assert.Contains("Cycle", await cycle.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await scenario.Admin.PutAsJsonAsync($"/api/categories/{a.Id}",
            new UpdateCategoryRequest(a.Name, a.Slug, null, c.Id))).StatusCode);
        var moved = await scenario.Admin.PostAsJsonAsync($"/api/admin/categories/{b.Id}/move", new MoveCategoryRequest(d.Id, snapshot.Version));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode); var receipt = (await moved.Content.ReadFromJsonAsync<CategoryMoveResponse>())!;
        Assert.Equal(snapshot.Version + 1, receipt.TreeVersion); Assert.Equal(b.Name, receipt.Category.Name); Assert.Equal(b.Slug, receipt.Category.Slug);
        using var anonymous = scenario.App.CreateClient();
        var breadcrumbs = (await anonymous.GetFromJsonAsync<CategoryTreeNode[]>($"/api/categories/{c.Id}/breadcrumbs"))!;
        Assert.Equal(new[] { d.Id, b.Id, c.Id }, breadcrumbs.Select(n => n.Id)); Assert.Equal(new int?[] { 1, 2, 3 }, breadcrumbs.Select(n => n.Depth));
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PostAsJsonAsync($"/api/admin/categories/{a.Id}/move", new MoveCategoryRequest(d.Id, snapshot.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await scenario.Admin.PostAsJsonAsync($"/api/admin/categories/{a.Id}/move", new MoveCategoryRequest(Guid.NewGuid(), receipt.TreeVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync($"/api/admin/categories/{a.Id}/move", new { newParentCategoryId = d.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync($"/api/admin/categories/{a.Id}/move", new { expectedTreeVersion = receipt.TreeVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.DeleteAsync($"/api/categories/{c.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.DeleteAsync($"/api/categories/{d.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Admin.DeleteAsync($"/api/categories/{a.Id}")).StatusCode);
        var afterDelete = (await scenario.Admin.GetFromJsonAsync<CategoryTreeSnapshot>("/api/admin/category-tree"))!;
        Assert.Equal(receipt.TreeVersion + 1, afterDelete.Version);
        var rootMove = await scenario.Admin.PostAsJsonAsync($"/api/admin/categories/{b.Id}/move", new MoveCategoryRequest(null, afterDelete.Version));
        Assert.Equal(HttpStatusCode.OK, rootMove.StatusCode);
        var rootReceipt = (await rootMove.Content.ReadFromJsonAsync<CategoryMoveResponse>())!;
        Assert.Equal(new[] { b.Id, c.Id }, (await anonymous.GetFromJsonAsync<CategoryTreeNode[]>($"/api/categories/{c.Id}/breadcrumbs"))!.Select(n => n.Id));
        (await scenario.Admin.PutAsJsonAsync($"/api/categories/{c.Id}", new UpdateCategoryRequest(c.Name, c.Slug, null, d.Id))).EnsureSuccessStatusCode();
        Assert.Equal(rootReceipt.TreeVersion + 1, (await scenario.Admin.GetFromJsonAsync<CategoryTreeSnapshot>("/api/admin/category-tree"))!.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PostAsJsonAsync($"/api/admin/categories/{b.Id}/move", new MoveCategoryRequest(d.Id, rootReceipt.TreeVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.GetAsync("/api/admin/category-tree")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/category-tree")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.PostAsJsonAsync($"/api/admin/categories/{b.Id}/move", new MoveCategoryRequest(null, 0))).StatusCode);
        await scenario.Db(async db => Assert.Equal(c.Id, (await db.Products.SingleAsync(p => p.Id == productId)).CategoryId));
    }

    [Fact]
    public async Task Depth_limit_accounts_for_descendants_and_legacy_cycles_are_diagnostic_not_infinite_paths()
    {
        using var scenario = await ReportTestScenario.Create(); Guid depthNine = default; Guid branch = default; Guid leaf = default; Guid root = default;
        await scenario.Db(async db =>
        {
            Guid? parent = null;
            for (var i = 1; i <= 9; i++)
            {
                var node = new Category { Name = "Depth " + i, Slug = "tree-depth-" + i, ParentCategoryId = parent };
                db.Categories.Add(node); await db.SaveChangesAsync(); parent = node.Id; if (i == 1) root = node.Id;
            }
            depthNine = parent!.Value;
            var b = new Category { Name = "Branch", Slug = "tree-branch" }; branch = b.Id;
            var l = new Category { Name = "Leaf", Slug = "tree-leaf", ParentCategoryId = b.Id }; leaf = l.Id;
            db.Categories.AddRange(b, l); await db.SaveChangesAsync();
        });
        var snapshot = (await scenario.Admin.GetFromJsonAsync<CategoryTreeSnapshot>("/api/admin/category-tree"))!;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await scenario.Admin.PostAsJsonAsync($"/api/admin/categories/{branch}/move",
            new MoveCategoryRequest(depthNine, snapshot.Version))).StatusCode);
        var tenth = await scenario.Admin.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Tenth", "tree-tenth", null, depthNine));
        Assert.Equal(HttpStatusCode.Created, tenth.StatusCode); var tenthId = (await tenth.Content.ReadFromJsonAsync<CategoryResponse>())!.Id;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await scenario.Admin.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Eleventh", "tree-eleventh", null, tenthId))).StatusCode);
        await scenario.Db(async db => { (await db.Categories.SingleAsync(c => c.Id == branch)).ParentCategoryId = leaf; await db.SaveChangesAsync(); });
        var invalid = (await scenario.Admin.GetFromJsonAsync<CategoryTreeSnapshot>("/api/admin/category-tree"))!;
        Assert.False(invalid.IsValid); Assert.Contains(invalid.Issues, i => i.Code == "Cycle");
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.GetAsync("/api/admin/category-tree/integrity")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await scenario.Admin.GetAsync($"/api/categories/{leaf}/breadcrumbs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.GetAsync($"/api/categories/{root}/breadcrumbs")).StatusCode);
        await scenario.Db(async db => Assert.Equal(leaf, (await db.Categories.SingleAsync(c => c.Id == branch)).ParentCategoryId));
    }

    [Fact]
    public async Task Five_thousand_is_supported_and_larger_trees_fail_clearly_without_partial_edits()
    {
        using var scenario = await ReportTestScenario.Create();
        await scenario.Db(async db =>
        {
            var count = await db.Categories.CountAsync();
            db.Categories.AddRange(Enumerable.Range(count, 5000 - count).Select(i => new Category { Name = "Bound " + i, Slug = "tree-bound-" + i }));
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.GetAsync("/api/admin/category-tree")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await scenario.Admin.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Overflow", "tree-overflow", null, null))).StatusCode);
        await scenario.Db(async db => { Assert.Equal(5000, await db.Categories.CountAsync()); db.Categories.Add(new Category { Name = "Legacy overflow", Slug = "tree-legacy-overflow" }); await db.SaveChangesAsync(); });
        var rejected = await scenario.Admin.GetAsync("/api/admin/category-tree"); Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Contains("CategoryLimitExceeded", await rejected.Content.ReadAsStringAsync());
    }
}
