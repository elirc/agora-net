using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class CartMergeApiTests
{
    [Fact]
    public async Task Merge_covers_all_overlap_states_retains_target_ids_and_clears_source_without_stock_changes()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "merge");
        var source = new Cart();
        var target = new Cart { CustomerId = owner.Id };
        Guid[] ids = [];
        await scenario.Db(async db =>
        {
            var variants = await db.ProductVariants.Include(v => v.Inventory).Where(v => v.Inventory != null && v.Product!.IsActive).OrderBy(v => v.Id).Take(6).ToListAsync();
            ids = variants.Select(v => v.Id).ToArray();
            foreach (var variant in variants) { variant.Price = new Money(10); variant.Inventory!.SetStock(100); }
            variants[1].Inventory!.SetStock(0); variants[4].Inventory!.SetStock(0);
            target.AddItem(ids[0], 2);
            target.SaveForLater(target.AddItem(ids[1], 1).Id);
            target.SaveForLater(target.AddItem(ids[2], 2).Id);
            target.AddItem(ids[3], 1); target.AddItem(ids[5], 3);
            source.SaveForLater(source.AddItem(ids[0], 3).Id);
            source.SaveForLater(source.AddItem(ids[1], 2).Id);
            source.AddItem(ids[2], 1); source.AddItem(ids[3], 1);
            source.SaveForLater(source.AddItem(ids[4], 2).Id);
            db.Carts.AddRange(source, target); await db.SaveChangesAsync();
        });
        var originalIds = target.Items.ToDictionary(i => i.ProductVariantId, i => i.Id);
        var sourceVersion = source.Version; var targetVersion = target.Version;
        var request = new MergeCartsRequest(source.Token, target.Token, sourceVersion, targetVersion);
        var response = await owner.Client.PostAsJsonAsync("/api/me/carts/merge", request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<CartMergeResponse>())!;
        Assert.Equal(sourceVersion + 1, result.SourceVersion); Assert.Equal(targetVersion + 1, result.TargetVersion);
        Assert.Equal(result.TargetVersion, result.Target.Version);
        Assert.Equal(13, result.Target.TotalQuantity);
        var active = result.Target.Items.ToDictionary(i => i.ProductVariantId);
        var saved = result.Target.SavedItems.ToDictionary(i => i.ProductVariantId);
        Assert.Equal((5, 3, 2, 3), (active[ids[0]].Quantity, active[ids[2]].Quantity, active[ids[3]].Quantity, active[ids[5]].Quantity));
        Assert.Equal((3, 2), (saved[ids[1]].Quantity, saved[ids[4]].Quantity));
        foreach (var item in result.Target.Items.Concat(result.Target.SavedItems))
            if (originalIds.TryGetValue(item.ProductVariantId, out var original)) Assert.Equal(original, item.Id);
        await scenario.Db(async db =>
        {
            var empty = await db.Carts.Include(c => c.Items).SingleAsync(c => c.Id == source.Id);
            Assert.Empty(empty.Items); Assert.Null(empty.CustomerId);
            Assert.Equal(sourceVersion + 1, empty.Version);
            Assert.Equal(scenario.Clock.Instant, empty.UpdatedAt);
            var stock = await db.InventoryItems.Where(i => ids.Contains(i.ProductVariantId)).ToListAsync();
            Assert.All(stock, i => Assert.Equal(0, i.QuantityReserved));
            Assert.Equal(0, stock.Single(i => i.ProductVariantId == ids[1]).QuantityOnHand);
            Assert.Equal(100, stock.Single(i => i.ProductVariantId == ids[0]).QuantityOnHand);
        });
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync("/api/me/carts/merge", request)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsJsonAsync("/api/me/carts/merge",
            request with { ExpectedSourceVersion = result.SourceVersion, ExpectedTargetVersion = result.TargetVersion })).StatusCode);
    }

    [Fact]
    public async Task Rejections_preserve_both_carts_and_hide_foreign_ownership()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "merge-owner");
        using var other = await AccountTestHelpers.Create(scenario, "merge-other");
        var source = new Cart { CustomerId = owner.Id }; var target = new Cart { CustomerId = owner.Id };
        var foreign = new Cart { CustomerId = other.Id };
        Guid variantId = default;
        await scenario.Db(async db =>
        {
            var variant = await db.ProductVariants.Include(v => v.Inventory).FirstAsync(v => v.Inventory != null && v.Product!.IsActive);
            variantId = variant.Id; variant.Inventory!.SetStock(100);
            source.AddItem(variantId, 40); target.AddItem(variantId, 60); foreign.AddItem(variantId, 1);
            db.Carts.AddRange(source, target, foreign); await db.SaveChangesAsync();
        });
        const string path = "/api/me/carts/merge";
        var request = new MergeCartsRequest(source.Token, target.Token, source.Version, target.Version);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsJsonAsync(path, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(path, request with { ExpectedSourceVersion = source.Version + 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(path, request with { ExpectedTargetVersion = target.Version + 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync(path, request with { SourceToken = target.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PostAsJsonAsync(path, request with { SourceToken = foreign.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PostAsJsonAsync(path, request with { TargetToken = foreign.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync(path, request with { ExpectedTargetVersion = null })).StatusCode);
        using var visitor = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PostAsJsonAsync(path, request)).StatusCode);
        await scenario.Db(async db =>
        {
            var carts = await db.Carts.Include(c => c.Items).Where(c => c.Id == source.Id || c.Id == target.Id).ToListAsync();
            Assert.Equal(40, Assert.Single(carts.Single(c => c.Id == source.Id).Items).Quantity);
            Assert.Equal(60, Assert.Single(carts.Single(c => c.Id == target.Id).Items).Quantity);
            Assert.Equal(source.Version, carts.Single(c => c.Id == source.Id).Version);
            Assert.Equal(target.Version, carts.Single(c => c.Id == target.Id).Version);
            var a = carts.Single(c => c.Id == source.Id); var b = carts.Single(c => c.Id == target.Id);
            a.UpdateItemQuantity(a.Items[0].Id, 1); b.UpdateItemQuantity(b.Items[0].Id, 1);
            (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == variantId)).SetStock(1);
            await db.SaveChangesAsync();
            request = request with { ExpectedSourceVersion = a.Version, ExpectedTargetVersion = b.Version };
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsJsonAsync(path, request)).StatusCode);
    }

    [Fact]
    public async Task Saved_currency_is_checked_and_claim_and_catalog_cascade_advance_parent_revisions()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "merge-audit");
        var source = new Cart(); var target = new Cart { CustomerId = owner.Id };
        Product product = null!;
        await scenario.Db(async db =>
        {
            var categoryId = (await db.Categories.FirstAsync()).Id;
            product = new Product { Name = "Merge audit", Slug = "merge-" + Guid.NewGuid().ToString("N"), CategoryId = categoryId };
            var usd = new ProductVariant { ProductId = product.Id, Sku = "MERGE-USD", Name = "USD", Price = new Money(10, "USD") };
            var eur = new ProductVariant { ProductId = product.Id, Sku = "MERGE-EUR", Name = "EUR", Price = new Money(10, "EUR") };
            usd.Inventory = new InventoryItem(usd.Id, 100); eur.Inventory = new InventoryItem(eur.Id, 0);
            product.Variants.AddRange([usd, eur]); db.Products.Add(product);
            source.AddItem(usd.Id, 1); target.SaveForLater(target.AddItem(eur.Id, 1).Id);
            db.Carts.AddRange(source, target); await db.SaveChangesAsync();
        });
        var request = new MergeCartsRequest(source.Token, target.Token, source.Version, target.Version);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsJsonAsync("/api/me/carts/merge", request)).StatusCode);
        var claim = await owner.Client.PostAsync($"/api/carts/{source.Token}/claim", null); claim.EnsureSuccessStatusCode();
        Assert.Equal(source.Version + 1, (await claim.Content.ReadFromJsonAsync<CartResponse>())!.Version);
        var repeatClaim = await owner.Client.PostAsync($"/api/carts/{source.Token}/claim", null); repeatClaim.EnsureSuccessStatusCode();
        Assert.Equal(source.Version + 1, (await repeatClaim.Content.ReadFromJsonAsync<CartResponse>())!.Version);
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Admin.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
        await scenario.Db(async db =>
        {
            var carts = await db.Carts.Include(c => c.Items).Where(c => c.Id == source.Id || c.Id == target.Id).ToListAsync();
            Assert.All(carts, c => Assert.Empty(c.Items));
            Assert.Equal(source.Version + 2, carts.Single(c => c.Id == source.Id).Version);
            Assert.Equal(target.Version + 1, carts.Single(c => c.Id == target.Id).Version);
        });
    }
}
