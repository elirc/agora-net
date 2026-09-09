using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class CartTemplatesApiTests
{
    private const string Path = "/api/me/cart-templates";

    [Fact]
    public async Task Template_stores_only_active_intent_and_applies_live_prices_activating_saved_overlap()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "template");
        var source = new Cart { CustomerId = owner.Id }; var target = new Cart { CustomerId = owner.Id };
        Guid[] ids = [];
        await scenario.Db(async db =>
        {
            var variants = await db.ProductVariants.Include(v => v.Inventory).Where(v => v.Inventory != null && v.Product!.IsActive).OrderBy(v => v.Id).Take(3).ToListAsync();
            ids = variants.Select(v => v.Id).ToArray();
            foreach (var v in variants) { v.Price = new Money(10); v.Inventory!.SetStock(100); }
            source.AddItem(ids[0], 2); source.AddItem(ids[1], 1); source.SaveForLater(source.AddItem(ids[2], 8).Id);
            target.SaveForLater(target.AddItem(ids[0], 3).Id);
            db.Carts.AddRange(source, target); await db.SaveChangesAsync();
        });
        var originalLine = target.Items.Single().Id;
        var created = await owner.Client.PostAsJsonAsync(Path, new CreateCartTemplateRequest("  Weekly  ", source.Token));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var template = (await created.Content.ReadFromJsonAsync<CartTemplateResponse>())!;
        Assert.Equal("Weekly", template.Name); Assert.Equal(2, template.Lines.Count);
        Assert.DoesNotContain(template.Lines, l => l.VariantId == ids[2]);
        using (var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
        {
            var properties = json.RootElement.GetProperty("lines")[0].EnumerateObject().Select(p => p.Name).Order().ToArray();
            Assert.Equal(new[] { "id", "productName", "quantity", "sku", "variantId", "variantName" }, properties);
        }
        await scenario.Db(async db =>
        {
            var variants = await db.ProductVariants.Where(v => ids.Contains(v.Id)).ToListAsync();
            foreach (var v in variants) v.Price = new Money(12);
            await db.SaveChangesAsync();
        });
        var applied = await owner.Client.PostAsJsonAsync($"{Path}/{template.Id}/apply", new ApplyCartTemplateRequest(target.Token, target.Version));
        Assert.True(applied.IsSuccessStatusCode, await applied.Content.ReadAsStringAsync());
        var cart = (await applied.Content.ReadFromJsonAsync<CartResponse>())!;
        Assert.Empty(cart.SavedItems); Assert.Equal(6, cart.TotalQuantity); Assert.Equal(72m, cart.Subtotal.Amount);
        Assert.All(cart.Items, line => Assert.Equal(12m, line.UnitPrice.Amount));
        Assert.Equal(originalLine, cart.Items.Single(i => i.ProductVariantId == ids[0]).Id);
        Assert.Equal(target.Version + 1, cart.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync($"{Path}/{template.Id}/apply",
            new ApplyCartTemplateRequest(target.Token, target.Version))).StatusCode);
        var again = (await owner.Client.GetFromJsonAsync<CartTemplateResponse>($"{Path}/{template.Id}"))!;
        Assert.Equal(template.Lines.ToArray(), again.Lines.ToArray());
        await scenario.Db(async db =>
        {
            Assert.Equal(source.Version, (await db.Carts.SingleAsync(c => c.Id == source.Id)).Version);
            var inventory = await db.InventoryItems.Where(i => ids.Contains(i.ProductVariantId)).ToListAsync();
            Assert.All(inventory, i => { Assert.Equal(100, i.QuantityOnHand); Assert.Equal(0, i.QuantityReserved); });
            Assert.Empty(await db.Orders.ToListAsync());
        });
    }

    [Fact]
    public async Task Missing_variant_keeps_snapshot_and_rejects_whole_apply_and_foreign_resources_are_hidden()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "template-owner");
        using var other = await AccountTestHelpers.Create(scenario, "template-other");
        var source = new Cart { CustomerId = owner.Id }; var target = new Cart { CustomerId = owner.Id };
        var foreign = new Cart { CustomerId = other.Id }; Guid removed = default;
        await scenario.Db(async db =>
        {
            var variants = await db.ProductVariants.Include(v => v.Inventory).Where(v => v.Inventory != null && v.Product!.IsActive).Take(2).ToListAsync();
            removed = variants[0].Id;
            foreach (var v in variants) { v.Inventory!.SetStock(100); source.AddItem(v.Id, 1); }
            db.Carts.AddRange(source, target, foreign); await db.SaveChangesAsync();
        });
        var template = (await (await owner.Client.PostAsJsonAsync(Path, new CreateCartTemplateRequest("Missing", source.Token)))
            .Content.ReadFromJsonAsync<CartTemplateResponse>())!;
        var snapshot = template.Lines.Single(l => l.VariantId == removed);
        await scenario.Db(async db => { db.ProductVariants.Remove(await db.ProductVariants.SingleAsync(v => v.Id == removed)); await db.SaveChangesAsync(); });
        var response = await owner.Client.PostAsJsonAsync($"{Path}/{template.Id}/apply", new ApplyCartTemplateRequest(target.Token, target.Version));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var problem = json.RootElement.GetProperty("lines")[0];
            Assert.Equal(snapshot.Id, problem.GetProperty("templateLineId").GetGuid());
            Assert.Equal(snapshot.Sku, problem.GetProperty("sku").GetString());
        }
        Assert.Equal(2, (await owner.Client.GetFromJsonAsync<CartTemplateResponse>($"{Path}/{template.Id}"))!.Lines.Count);
        await scenario.Db(async db => { var cart = await db.Carts.Include(c => c.Items).SingleAsync(c => c.Id == target.Id); Assert.Empty(cart.Items); Assert.Equal(target.Version, cart.Version); });
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"{Path}/{template.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.DeleteAsync($"{Path}/{template.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"{Path}/{template.Id}/apply", new ApplyCartTemplateRequest(foreign.Token, foreign.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PostAsJsonAsync($"{Path}/{template.Id}/apply", new ApplyCartTemplateRequest(foreign.Token, foreign.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync(Path, new CreateCartTemplateRequest("Foreign", source.Token))).StatusCode);
        Assert.Empty((await other.Client.GetFromJsonAsync<CartTemplateSummary[]>(Path))!);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"{Path}/{template.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"{Path}/{template.Id}")).StatusCode);
        await scenario.Db(async db => Assert.Empty(await db.CartTemplateLines.ToListAsync()));
        using var anonymous = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path)).StatusCode);
    }

    [Fact]
    public async Task Quantity_stock_activity_currency_and_capacity_failures_do_not_partially_write()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "template-rules");
        var source = new Cart { CustomerId = owner.Id }; var target = new Cart { CustomerId = owner.Id }; var empty = new Cart { CustomerId = owner.Id };
        Guid id = default; Guid secondId = default;
        await scenario.Db(async db =>
        {
            var variants = await db.ProductVariants.Include(v => v.Inventory).Where(v => v.Inventory != null && v.Product!.IsActive).Take(2).ToListAsync();
            id = variants[0].Id; secondId = variants[1].Id;
            foreach (var v in variants) { v.Price = new Money(10); v.Inventory!.SetStock(100); }
            source.AddItem(id, 2); target.AddItem(id, 98); target.SaveForLater(target.AddItem(secondId, 1).Id);
            db.Carts.AddRange(source, target, empty); await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync(Path, new CreateCartTemplateRequest(" ", source.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync(Path, new CreateCartTemplateRequest("Empty", empty.Token))).StatusCode);
        var template = (await (await owner.Client.PostAsJsonAsync(Path, new CreateCartTemplateRequest("Rules", source.Token))).Content.ReadFromJsonAsync<CartTemplateResponse>())!;
        var expectedVersion = target.Version;
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync($"{Path}/{template.Id}/apply", new { targetCartToken = target.Token })).StatusCode);
        async Task Reject() => Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await owner.Client.PostAsJsonAsync($"{Path}/{template.Id}/apply", new ApplyCartTemplateRequest(target.Token, expectedVersion))).StatusCode);
        await Reject(); // 98 + 2 exceeds the line limit.
        await scenario.Db(async db =>
        {
            var cart = await db.Carts.Include(c => c.Items).SingleAsync(c => c.Id == target.Id);
            cart.UpdateItemQuantity(cart.Items.Single(i => i.ProductVariantId == id).Id, 1); expectedVersion = cart.Version;
            (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == id)).SetStock(2); await db.SaveChangesAsync();
        });
        await Reject(); // 1 + 2 exceeds available stock.
        await scenario.Db(async db =>
        {
            (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == id)).SetStock(100);
            var variant = await db.ProductVariants.Include(v => v.Product).SingleAsync(v => v.Id == id); variant.Product!.IsActive = false;
            await db.SaveChangesAsync();
        });
        await Reject();
        await scenario.Db(async db =>
        {
            var variant = await db.ProductVariants.Include(v => v.Product).SingleAsync(v => v.Id == id); variant.Product!.IsActive = true;
            (await db.ProductVariants.SingleAsync(v => v.Id == secondId)).Price = new Money(10, "EUR"); await db.SaveChangesAsync();
        });
        await Reject(); // Saved lines also participate in the one-currency rule.
        for (var i = 1; i < 10; i++) Assert.Equal(HttpStatusCode.Created,
            (await owner.Client.PostAsJsonAsync(Path, new CreateCartTemplateRequest("Template " + i, source.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(Path, new CreateCartTemplateRequest("Eleven", source.Token))).StatusCode);
        var list = (await owner.Client.GetFromJsonAsync<CartTemplateSummary[]>(Path))!;
        Assert.Equal(10, list.Length); Assert.All(list, row => Assert.Equal(1, row.LineCount));
        Assert.Equal(list.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(t => t.Id), list.Select(t => t.Id));
        await owner.Client.DeleteAsync($"{Path}/{template.Id}");
        Assert.Equal(HttpStatusCode.Created, (await owner.Client.PostAsJsonAsync(Path, new CreateCartTemplateRequest("Replacement", source.Token))).StatusCode);
        await scenario.Db(async db =>
        {
            var cart = await db.Carts.Include(c => c.Items).SingleAsync(c => c.Id == target.Id);
            Assert.Equal(expectedVersion, cart.Version); Assert.Equal(1, cart.Items.Single(i => i.ProductVariantId == id).Quantity);
            Assert.True(cart.Items.Single(i => i.ProductVariantId == secondId).IsSavedForLater);
        });
    }
}
