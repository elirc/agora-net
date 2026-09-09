using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class OrderReorderApiTests
{
    [Fact]
    public async Task Reorder_after_checkout_uses_current_identity_and_price_without_rewriting_purchase_or_stock()
    {
        using var scenario = await ReportTestScenario.Create();
        using var account = await AccountTestHelpers.Create(scenario, "reorder");
        Guid variantId = default;
        await scenario.Db(async db =>
        {
            var variant = await db.ProductVariants.Include(v => v.Inventory).FirstAsync(v => v.Inventory != null && v.Product!.IsActive);
            variantId = variant.Id; variant.Price = new Money(15); variant.Inventory!.SetStock(50);
            await db.SaveChangesAsync();
        });
        var created = await account.Client.PostAsync("/api/carts", null); created.EnsureSuccessStatusCode();
        var cart = (await created.Content.ReadFromJsonAsync<CartResponse>())!;
        (await account.Client.PostAsJsonAsync($"/api/carts/{cart.Token}/items", new AddCartItemRequest(variantId, 2))).EnsureSuccessStatusCode();
        var checkout = await account.Client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(cart.Token, account.Email,
            new AddressDto("Repeat Buyer", "1 Original Street", null, "Town", "Region", "12345", "US"), null, "tok_visa"));
        checkout.EnsureSuccessStatusCode();
        var order = (await checkout.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal(15m, Assert.Single(order.Items).UnitPrice);
        await scenario.Db(async db =>
        {
            var variant = await db.ProductVariants.SingleAsync(v => v.Id == variantId);
            variant.Edit("Today's variant", 18, variant.WeightGrams, variant.Options);
            variant.Sku = "NEW-SKU-" + Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
        });
        var path = $"/api/me/orders/{order.Number}/reorder";
        var first = await account.Client.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var repeated = (await first.Content.ReadFromJsonAsync<CartResponse>())!;
        var item = Assert.Single(repeated.Items);
        Assert.Equal(variantId, item.ProductVariantId); Assert.Equal(18m, item.UnitPrice.Amount);
        Assert.StartsWith("NEW-SKU-", item.Sku); Assert.Equal("Today's variant", item.VariantName);
        Assert.Empty(repeated.SavedItems); Assert.Equal(2, item.Quantity);
        var second = await account.Client.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(repeated.Token, (await second.Content.ReadFromJsonAsync<CartResponse>())!.Token);
        await scenario.Db(async db =>
        {
            Assert.Equal(1, await db.Orders.CountAsync(o => o.CustomerId == account.Id));
            var purchase = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Number == order.Number);
            Assert.Equal(15m, Assert.Single(purchase.Items).UnitPrice);
            Assert.Equal(OrderStatus.Paid, purchase.Status);
            Assert.Equal(account.Id, (await db.Carts.SingleAsync(c => c.Token == repeated.Token)).CustomerId);
            var stock = await db.InventoryItems.SingleAsync(i => i.ProductVariantId == variantId);
            Assert.Equal((48, 0), (stock.QuantityOnHand, stock.QuantityReserved));
        });
    }

    [Fact]
    public async Task Missing_historical_identity_does_not_match_reused_sku_or_save_a_valid_subset()
    {
        using var scenario = await ReportTestScenario.Create();
        using var account = await AccountTestHelpers.Create(scenario, "reorder-missing");
        var order = PackingSlipApiTests.NewOrder(); order.CustomerId = account.Id; order.MarkPaid("pay", scenario.Clock.Instant);
        var historicalId = Guid.NewGuid(); order.Items[0].ProductVariantId = historicalId; order.Items[0].Sku = "REUSED";
        await scenario.Db(async db =>
        {
            var variants = await db.ProductVariants.Include(v => v.Inventory).Where(v => v.Inventory != null && v.Product!.IsActive).Take(2).ToListAsync();
            variants[0].Sku = "REUSED"; variants[0].Inventory!.SetStock(100); variants[1].Inventory!.SetStock(100);
            order.Items.Add(new OrderItem { OrderId = order.Id, ProductVariantId = variants[1].Id, Sku = "VALID-SNAPSHOT", Quantity = 2 });
            db.Orders.Add(order); await db.SaveChangesAsync();
        });
        var response = await account.Client.PostAsync($"/api/me/orders/{order.Number}/reorder", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("REUSED", json); Assert.Contains("Variant no longer exists", json);
        await scenario.Db(async db => Assert.False(await db.Carts.AnyAsync(c => c.CustomerId == account.Id)));
    }

    [Fact]
    public async Task Ownership_status_grouped_quantity_stock_activity_currency_and_line_limits_reject_atomically()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "reorder-owner");
        using var other = await AccountTestHelpers.Create(scenario, "reorder-other");
        var pending = PackingSlipApiTests.NewOrder(); pending.CustomerId = owner.Id;
        var cancelled = PackingSlipApiTests.NewOrder(); cancelled.CustomerId = owner.Id; cancelled.Cancel(scenario.Clock.Instant);
        var grouped = PackingSlipApiTests.NewOrder(); grouped.CustomerId = owner.Id; grouped.MarkPaid("pay", scenario.Clock.Instant); grouped.Items[0].Quantity = 60;
        var unavailable = PackingSlipApiTests.NewOrder(); unavailable.CustomerId = owner.Id; unavailable.MarkPaid("pay", scenario.Clock.Instant);
        var mixed = PackingSlipApiTests.NewOrder(); mixed.CustomerId = owner.Id; mixed.MarkPaid("pay", scenario.Clock.Instant);
        var huge = PackingSlipApiTests.NewOrder(); huge.CustomerId = owner.Id; huge.MarkPaid("pay", scenario.Clock.Instant); huge.Items.Clear();
        var guest = PackingSlipApiTests.NewOrder(); guest.Email = owner.Email;
        await scenario.Db(async db =>
        {
            var variants = await db.ProductVariants.Include(v => v.Inventory).Where(v => v.Inventory != null && v.Product!.IsActive).Take(3).ToListAsync();
            variants[0].Inventory!.SetStock(100); variants[1].Inventory!.SetStock(0); variants[2].Inventory!.SetStock(100);
            variants[0].Price = new Money(10, "USD"); variants[2].Price = new Money(10, "EUR");
            pending.Items[0].ProductVariantId = variants[0].Id; cancelled.Items[0].ProductVariantId = variants[0].Id;
            grouped.Items[0].ProductVariantId = variants[0].Id;
            grouped.Items.Add(new OrderItem { OrderId = grouped.Id, ProductVariantId = variants[0].Id, Sku = "SAME-ID", Quantity = 40 });
            unavailable.Items[0].ProductVariantId = variants[1].Id;
            mixed.Items[0].ProductVariantId = variants[0].Id;
            mixed.Items.Add(new OrderItem { OrderId = mixed.Id, ProductVariantId = variants[2].Id, Sku = "EUR", Quantity = 1 });
            for (var i = 0; i < 51; i++) huge.Items.Add(new OrderItem { OrderId = huge.Id, ProductVariantId = Guid.NewGuid(), Sku = $"OLD-{i}", Quantity = 1 });
            db.Orders.AddRange(pending, cancelled, grouped, unavailable, mixed, huge, guest); await db.SaveChangesAsync();
        });
        string Path(Order order) => $"/api/me/orders/{order.Number}/reorder";
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync(Path(cancelled), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PostAsync(Path(guest), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsync(Path(pending), null)).StatusCode);
        foreach (var order in new[] { grouped, unavailable, mixed, huge })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsync(Path(order), null)).StatusCode);
        await scenario.Db(async db => Assert.False(await db.Carts.AnyAsync(c => c.CustomerId == owner.Id)));
        Assert.Equal(HttpStatusCode.Created, (await owner.Client.PostAsync(Path(cancelled), null)).StatusCode);
        await scenario.Db(async db =>
        {
            var variant = await db.ProductVariants.Include(v => v.Product).SingleAsync(v => v.Id == cancelled.Items[0].ProductVariantId);
            variant.Product!.IsActive = false; await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsync(Path(cancelled), null)).StatusCode);
        using var visitor = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PostAsync(Path(cancelled), null)).StatusCode);
    }
}
