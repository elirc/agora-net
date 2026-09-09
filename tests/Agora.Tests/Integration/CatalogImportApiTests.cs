using System.Net;
using System.Net.Http.Json;
using System.Text;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class CatalogImportApiTests
{
    [Fact]
    public async Task Preview_stages_only_commit_creates_inactive_zero_stock_graphs_and_matching_replay_returns_original_receipt()
    {
        using var scenario = await ReportTestScenario.Create(); Guid category = default; int products = 0; int inventories = 0;
        await scenario.Db(async db => { category = await db.Categories.Select(c => c.Id).FirstAsync(); products = await db.Products.CountAsync(); inventories = await db.InventoryItems.CountAsync(); });
        var request = Request(category, "clean");
        var preview = await Preview(scenario, request);
        var feedBefore = await scenario.Admin.GetFromJsonAsync<CatalogBootstrapResult>(
            "/api/admin/catalog-sync/bootstrap");
        Assert.Equal("DraftValid", preview.State); Assert.Equal(0L, preview.Revision); Assert.Empty(preview.Errors); Assert.Empty(preview.Receipt);
        Assert.All(preview.Products, row => Assert.False(row.Product.IsActive)); Assert.Equal(TimeSpan.FromHours(24), preview.ExpiresAt - preview.CreatedAt);
        await scenario.Db(async db => { Assert.Equal(products, await db.Products.CountAsync()); Assert.Equal(inventories, await db.InventoryItems.CountAsync()); Assert.Single(await db.Set<CatalogImport>().ToListAsync()); });
        var appliedResponse = await Commit(scenario, preview);
        Assert.Equal(HttpStatusCode.OK, appliedResponse.StatusCode); Assert.Contains("no-store", appliedResponse.Headers.CacheControl!.ToString());
        var applied = (await appliedResponse.Content.ReadFromJsonAsync<CatalogImportView>())!;
        Assert.Equal("Applied", applied.State); Assert.Equal(1L, applied.Revision); Assert.Equal(2, applied.Receipt.Count);
        await scenario.Db(async db =>
        {
            var ids = applied.Receipt.Select(r => r.ProductId).ToArray();
            var rows = await db.Products.Include(p => p.Variants).ThenInclude(v => v.Inventory).Where(p => ids.Contains(p.Id)).ToListAsync();
            Assert.Equal(2, rows.Count); Assert.All(rows, p => { Assert.False(p.IsActive); Assert.All(p.Variants, v => Assert.Equal(0, v.Inventory!.QuantityOnHand)); });
            Assert.Equal(products + 2, await db.Products.CountAsync()); Assert.Equal(inventories + 2, await db.InventoryItems.CountAsync());
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CatalogImports SET ExpiresAt = {DateTimeOffset.UnixEpoch.UtcTicks} WHERE Id = {preview.Id}");
        });
        var replay = await Commit(scenario, preview); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(applied.Receipt, (await replay.Content.ReadFromJsonAsync<CatalogImportView>())!.Receipt);
        var feed = await scenario.Admin.GetFromJsonAsync<CatalogChangesResult>(
            $"/api/admin/catalog-sync/changes?after={feedBefore!.Watermark}&limit=100");
        Assert.Equal(applied.Receipt.Select(row => row.ProductId).Order(),
            feed!.Changes.Select(change => change.ProductId).Order());
        Assert.All(feed.Changes, change => Assert.Equal("Upsert", change.Kind));
        Assert.Equal(feed.HighWatermark, feed.LastDeliveredSequence);
        var feedReplay = await scenario.Admin.GetFromJsonAsync<CatalogChangesResult>(
            $"/api/admin/catalog-sync/changes?after={feed.LastDeliveredSequence}&limit=100");
        Assert.Empty(feedReplay!.Changes);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PostAsJsonAsync($"/api/admin/catalog-imports/{preview.Id}/commit", new CommitCatalogImportRequest(0, new string('0', 64)))).StatusCode);
        var read = (await scenario.Admin.GetFromJsonAsync<CatalogImportView>($"/api/admin/catalog-imports/{preview.Id}"))!;
        Assert.Equal(applied.Receipt, read.Receipt);
    }

    [Fact]
    public async Task Commit_revalidates_slug_and_removed_category_without_creating_even_the_first_valid_row()
    {
        using var scenario = await ReportTestScenario.Create(); Guid category = default;
        await scenario.Db(async db => { category = await db.Categories.Select(c => c.Id).FirstAsync(); });
        var proposal = Request(category, "contended"); var preview = await Preview(scenario, proposal);
        (await scenario.Admin.PostAsJsonAsync("/api/products", proposal.Products[1].Product)).EnsureSuccessStatusCode();
        var rejected = await Commit(scenario, preview); Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("SlugExists", await rejected.Content.ReadAsStringAsync());
        await scenario.Db(async db => { Assert.False(await db.Products.AnyAsync(p => p.Slug == "contended-a")); Assert.Equal(CatalogImportState.DraftValid, (await db.Set<CatalogImport>().SingleAsync()).State); Assert.Empty(await db.Set<CatalogImportResult>().ToListAsync()); });
        var newCategory = new Category { Name = "Temporary import", Slug = "temporary-import" };
        await scenario.Db(async db => { db.Categories.Add(newCategory); await db.SaveChangesAsync(); });
        var missing = await Preview(scenario, Request(newCategory.Id, "missing-category"));
        await scenario.Db(async db => { db.Categories.Remove(await db.Categories.SingleAsync(c => c.Id == newCategory.Id)); await db.SaveChangesAsync(); });
        var missingResult = await Commit(scenario, missing); Assert.Equal(HttpStatusCode.Conflict, missingResult.StatusCode); Assert.Contains("MissingCategory", await missingResult.Content.ReadAsStringAsync());
        await scenario.Db(async db => Assert.False(await db.Products.AnyAsync(p => p.Slug.StartsWith("missing-category"))));
    }

    [Fact]
    public async Task Duplicate_batch_identifiers_and_schema_errors_are_row_scoped_and_invalid_drafts_cannot_apply()
    {
        using var scenario = await ReportTestScenario.Create(); Guid category = default;
        await scenario.Db(async db =>
        {
            category = await db.Categories.Select(c => c.Id).FirstAsync();
            db.CategoryOptionSchemas.Add(new CategoryOptionSchema(category, CategoryOptionSchemaMode.Enforce, [new("size", true, ["M"])])); await db.SaveChangesAsync();
        });
        var request = Request(category, "invalid");
        request.Products[1] = request.Products[1] with { RowKey = " A ", Product = request.Products[1].Product with { Slug = request.Products[0].Product.Slug,
            Variants = [request.Products[0].Product.Variants[0] with { Sku = " INVALID-A " }] } };
        var preview = await Preview(scenario, request); Assert.Equal("DraftInvalid", preview.State);
        Assert.Contains(preview.Errors, e => e.Code == "DuplicateRowKey"); Assert.Contains(preview.Errors, e => e.Code == "DuplicateSlug");
        Assert.Contains(preview.Errors, e => e.Code == "DuplicateSku"); Assert.Contains(preview.Errors, e => e.Code == "RequiredKeyMissing");
        Assert.Equal(HttpStatusCode.Conflict, (await Commit(scenario, preview)).StatusCode);
        await scenario.Db(async db => Assert.False(await db.Products.AnyAsync(p => p.Slug == "invalid-a")));
    }

    [Fact]
    public async Task Bounds_authorization_revision_digest_and_expiry_are_explicit()
    {
        using var scenario = await ReportTestScenario.Create(); using var customer = await AccountTestHelpers.Create(scenario, "import-reader"); using var anonymous = scenario.App.CreateClient(); Guid category = default;
        await scenario.Db(async db => { category = await db.Categories.Select(c => c.Id).FirstAsync(); });
        var request = Request(category, "bounds");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/admin/catalog-imports/preview", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.PostAsJsonAsync("/api/admin/catalog-imports/preview", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync("/api/admin/catalog-imports/preview", request with { Version = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync("/api/admin/catalog-imports/preview", request with { Products = [] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync("/api/admin/catalog-imports/preview", request with { Products = Enumerable.Repeat(request.Products[0], 101).ToList() })).StatusCode);
        var tooMany = request.Products[0] with { Product = request.Products[0].Product with { Variants = Enumerable.Repeat(request.Products[0].Product.Variants[0], 301).ToList() } };
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync("/api/admin/catalog-imports/preview", request with { Products = [tooMany] })).StatusCode);
        using var oversized = new StringContent(new string(' ', 1_048_577), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await scenario.Admin.PostAsync("/api/admin/catalog-imports/preview", oversized)).StatusCode);
        var preview = await Preview(scenario, request);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync($"/api/admin/catalog-imports/{preview.Id}/commit", new { digest = preview.Digest })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PostAsJsonAsync($"/api/admin/catalog-imports/{preview.Id}/commit", new CommitCatalogImportRequest(9, preview.Digest))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.GetAsync($"/api/admin/catalog-imports/{preview.Id}")).StatusCode);
        await scenario.Db(async db => await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CatalogImports SET ExpiresAt = {scenario.Clock.Instant.UtcTicks} WHERE Id = {preview.Id}"));
        Assert.Equal(HttpStatusCode.Conflict, (await Commit(scenario, preview)).StatusCode);
        await scenario.Db(async db => Assert.False(await db.Products.AnyAsync(p => p.Slug.StartsWith("bounds-"))));
    }

    internal static PreviewCatalogImportRequest Request(Guid category, string prefix) => new(1,
        new[] { "a", "b" }.Select(suffix => new CatalogImportRowRequest(suffix.ToUpperInvariant(),
            new CreateProductRequest(category, " Import " + suffix + " ", prefix + "-" + suffix, "Description", true,
                [new CreateVariantRequest(prefix.ToUpperInvariant() + "-" + suffix.ToUpperInvariant(), " Default ", 12.50m, "usd", [])],
                [new CreateImageRequest("https://example.test/product.png", "Example", 0)]))).ToList());
    internal static async Task<CatalogImportView> Preview(ReportTestScenario scenario, PreviewCatalogImportRequest request)
    {
        var response = await scenario.Admin.PostAsJsonAsync("/api/admin/catalog-imports/preview", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<CatalogImportView>())!;
    }
    private static Task<HttpResponseMessage> Commit(ReportTestScenario scenario, CatalogImportView preview) =>
        scenario.Admin.PostAsJsonAsync($"/api/admin/catalog-imports/{preview.Id}/commit", new CommitCatalogImportRequest(preview.Revision, preview.Digest));
}
