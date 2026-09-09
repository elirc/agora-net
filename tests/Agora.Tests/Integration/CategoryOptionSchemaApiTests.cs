using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class CategoryOptionSchemaApiTests
{
    [Fact]
    public async Task Observe_reports_legacy_mismatches_then_enforce_rejects_new_writes_but_keeps_old_products_readable()
    {
        using var scenario = await ReportTestScenario.Create();
        var ids = await SeedCategoryWithProduct(scenario, new Dictionary<string, string> { ["size"] = "XL" });
        var absent = (await scenario.Admin.GetFromJsonAsync<CategoryOptionSchemaResponse>($"/api/admin/categories/{ids.CategoryId}/option-schema"))!;
        Assert.Equal("Off", absent.Mode); Assert.Null(absent.Revision); Assert.Empty(absent.Rules);

        var observe = await Put(scenario, ids.CategoryId, "Observe", null);
        Assert.Equal(0, observe.Revision); Assert.Equal("size", Assert.Single(observe.Rules).Key);
        var report = (await scenario.Admin.GetFromJsonAsync<PagedResult<CategoryOptionViolationResponse>>(
            $"/api/admin/categories/{ids.CategoryId}/option-schema/violations?page=1&pageSize=1"))!;
        var mismatch = Assert.Single(report.Items!); Assert.Equal(ids.VariantId, mismatch.VariantId);
        Assert.Single(mismatch.Violations, v => v.Reason == "ValueNotAllowed" && v.ActualValue == "XL");

        var enforced = await Put(scenario, ids.CategoryId, "Enforce", observe.Revision);
        Assert.Equal(1, enforced.Revision);
        var rejected = await scenario.Admin.PostAsJsonAsync("/api/products", ProductRequest(ids.CategoryId, "XL"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Contains("ValueNotAllowed", await rejected.Content.ReadAsStringAsync());
        await scenario.Db(async db => Assert.Equal(1, await db.Products.CountAsync(p => p.CategoryId == ids.CategoryId)));

        using var anonymous = scenario.App.CreateClient();
        var readable = await anonymous.GetAsync($"/api/products/{ids.ProductId}");
        Assert.Equal(HttpStatusCode.OK, readable.StatusCode);

        // Grandfathering is narrow: changing price with identical options is allowed,
        // but an option edit and a clone are new authoring decisions.
        var current = (await scenario.Admin.GetFromJsonAsync<AdminVariantResponse>(
            $"/api/admin/products/{ids.ProductId}/variants/{ids.VariantId}"))!;
        var priceOnly = await scenario.Admin.PutAsJsonAsync($"/api/admin/products/{ids.ProductId}/variants/{ids.VariantId}",
            new EditVariantRequest(current.Name, current.Price.Amount + 1, current.WeightGrams,
                new Dictionary<string, string>(current.Options), current.Version));
        Assert.Equal(HttpStatusCode.OK, priceOnly.StatusCode);
        var afterPrice = (await priceOnly.Content.ReadFromJsonAsync<AdminVariantResponse>())!;
        var optionEdit = await scenario.Admin.PutAsJsonAsync($"/api/admin/products/{ids.ProductId}/variants/{ids.VariantId}",
            new EditVariantRequest(afterPrice.Name, afterPrice.Price.Amount, afterPrice.WeightGrams,
                new Dictionary<string, string> { ["size"] = "XXL" }, afterPrice.Version));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, optionEdit.StatusCode);
        var clone = await scenario.Admin.PostAsJsonAsync($"/api/admin/products/{ids.ProductId}/clone", new CloneProductRequest(
            "Invalid clone", "schema-clone-" + Guid.NewGuid().ToString("N"),
            [new CloneVariantSkuRequest(ids.VariantId, "CLONE-" + Guid.NewGuid().ToString("N"))]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, clone.StatusCode);
    }

    [Fact]
    public async Task Enforce_validates_every_variant_before_inserting_any_and_revision_contract_is_exact()
    {
        using var scenario = await ReportTestScenario.Create();
        var category = await CreateCategory(scenario, "Schema atomic");
        var created = await Put(scenario, category.Id, "Enforce", null);
        Assert.Equal(0, created.Revision);

        var stale = await scenario.Admin.PutAsJsonAsync($"/api/admin/categories/{category.Id}/option-schema",
            SchemaBody("Off", null));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var missingRevision = await scenario.Admin.PutAsJsonAsync($"/api/admin/categories/{category.Id}/option-schema",
            new { mode = "Off", rules = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, missingRevision.StatusCode);
        var numericMode = await scenario.Admin.PutAsJsonAsync($"/api/admin/categories/{category.Id}/option-schema",
            new { mode = "2", rules = Array.Empty<object>(), expectedRevision = created.Revision });
        Assert.Equal(HttpStatusCode.BadRequest, numericMode.StatusCode);

        var body = ProductRequest(category.Id, "M", "XL");
        var response = await scenario.Admin.PostAsJsonAsync("/api/products", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await scenario.Db(async db => Assert.False(await db.Products.AnyAsync(p => p.Slug == body.Slug)));
    }

    [Fact]
    public async Task Violations_are_filtered_before_paging_and_admin_routes_are_private()
    {
        using var scenario = await ReportTestScenario.Create();
        var category = await CreateCategory(scenario, "Schema paging");
        await Put(scenario, category.Id, "Observe", null);
        await scenario.Db(async db =>
        {
            var tax = await db.TaxCategories.FirstAsync();
            foreach (var (sku, size) in new[] { ("A-valid", "M"), ("B-valid", "S"), ("C-bad", "XL"), ("D-bad", "") })
            {
                var p = Product(category.Id, tax.Id, sku, size); db.Products.Add(p);
            }
            await db.SaveChangesAsync();
        });
        var first = (await scenario.Admin.GetFromJsonAsync<PagedResult<CategoryOptionViolationResponse>>(
            $"/api/admin/categories/{category.Id}/option-schema/violations?page=1&pageSize=1"))!;
        var second = (await scenario.Admin.GetFromJsonAsync<PagedResult<CategoryOptionViolationResponse>>(
            $"/api/admin/categories/{category.Id}/option-schema/violations?page=2&pageSize=1"))!;
        Assert.Equal(2, first.TotalCount); Assert.Equal("C-bad", Assert.Single(first.Items!).Sku);
        Assert.Equal("D-bad", Assert.Single(second.Items!).Sku);
        Assert.Contains("private", (await scenario.Admin.GetAsync($"/api/admin/categories/{category.Id}/option-schema")).Headers.CacheControl!.ToString());
        using var anonymous = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/admin/categories/{category.Id}/option-schema")).StatusCode);
    }

    [Fact]
    public async Task Moving_a_product_validates_all_current_variants_against_the_destination_only()
    {
        using var scenario = await ReportTestScenario.Create();
        var source = await CreateCategory(scenario, "Schema source");
        var destination = await CreateCategory(scenario, "Schema destination");
        await Put(scenario, destination.Id, "Enforce", null);
        Guid productId = default;
        await scenario.Db(async db =>
        {
            var tax = await db.TaxCategories.FirstAsync(); var product = Product(source.Id, tax.Id, "MOVE-XL", "XL");
            db.Products.Add(product); await db.SaveChangesAsync(); productId = product.Id;
        });
        var current = (await scenario.Admin.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}"))!;
        var response = await scenario.Admin.PutAsJsonAsync($"/api/products/{productId}", new UpdateProductRequest(
            destination.Id, current.Name, current.Slug, current.Description, current.IsActive, current.TaxCategoryCode));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await scenario.Db(async db => Assert.Equal(source.Id, (await db.Products.SingleAsync(p => p.Id == productId)).CategoryId));
    }

    private static object SchemaBody(string mode, long? revision) => new
    {
        mode, expectedRevision = revision,
        rules = new[] { new { key = " SIZE ", required = true, allowedValues = new[] { "S", "M", "L" } } },
    };
    private static async Task<CategoryOptionSchemaResponse> Put(ReportTestScenario scenario, Guid categoryId, string mode, long? revision)
    {
        var response = await scenario.Admin.PutAsJsonAsync($"/api/admin/categories/{categoryId}/option-schema", SchemaBody(mode, revision));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<CategoryOptionSchemaResponse>())!;
    }
    private static async Task<CategoryResponse> CreateCategory(ReportTestScenario scenario, string name)
    {
        var response = await scenario.Admin.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(name, "schema-" + Guid.NewGuid().ToString("N"), null, null));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }
    private static CreateProductRequest ProductRequest(Guid categoryId, params string[] sizes) => new(categoryId, "Schema product",
        "schema-product-" + Guid.NewGuid().ToString("N"), "Teaching fixture", false,
        sizes.Select((size, i) => new CreateVariantRequest("SCHEMA-" + Guid.NewGuid().ToString("N"), "Choice " + i, 10, "USD",
            new Dictionary<string, string> { ["size"] = size }, 100)).ToList(), null);
    private static Product Product(Guid categoryId, Guid taxId, string sku, string size)
    {
        var p = new Product { CategoryId = categoryId, TaxCategoryId = taxId, Name = sku, Slug = "schema-" + Guid.NewGuid().ToString("N"), Description = "" };
        var v = new ProductVariant { ProductId = p.Id, Sku = sku, Name = sku, Price = new Money(10, "USD"), Options = new() { ["size"] = size } };
        v.Inventory = new InventoryItem(v.Id, 0); p.Variants.Add(v); return p;
    }
    private static async Task<(Guid CategoryId, Guid ProductId, Guid VariantId)> SeedCategoryWithProduct(ReportTestScenario scenario, Dictionary<string, string> options)
    {
        var result = (CategoryId: Guid.Empty, ProductId: Guid.Empty, VariantId: Guid.Empty);
        await scenario.Db(async db => { var category = new Category { Name = "Schema legacy", Slug = "schema-" + Guid.NewGuid().ToString("N") };
            var tax = await db.TaxCategories.FirstAsync(); var product = Product(category.Id, tax.Id, "LEGACY-XL", options["size"]);
            db.AddRange(category, product); await db.SaveChangesAsync(); result = (category.Id, product.Id, product.Variants.Single().Id); });
        return result;
    }
}
