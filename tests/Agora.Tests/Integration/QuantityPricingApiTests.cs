using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class QuantityPricingApiTests
{
    [Fact]
    public async Task Cart_quote_checkout_and_historical_return_agree_on_quantity_price()
    {
        using var scenario = await ReportTestScenario.Create(); using var owner = await AccountTestHelpers.Create(scenario, "tier-buyer");
        Guid variantId = default;
        await scenario.Db(async db => { var v = await db.ProductVariants.FirstAsync(v => v.Sku == "TEE-BLK-S"); variantId = v.Id; v.Price = new Money(10); await db.SaveChangesAsync(); });
        var path = $"/api/admin/variants/{variantId}/quantity-pricing";
        var policy = await scenario.Admin.PutAsJsonAsync(path, new PutQuantityPricingRequest(null, [new(5, 9), new(10, 8)]));
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode); Assert.Equal(0L, (await policy.Content.ReadFromJsonAsync<QuantityPricingResponse>())!.Revision);
        var cart = (await (await owner.Client.PostAsync("/api/carts", null)).Content.ReadFromJsonAsync<CartResponse>())!;
        var add = await owner.Client.PostAsJsonAsync($"/api/carts/{cart.Token}/items", new AddCartItemRequest(variantId, 4));
        cart = (await add.Content.ReadFromJsonAsync<CartResponse>())!; Assert.Equal(40, cart.Subtotal.Amount); Assert.Null(cart.Items.Single().SelectedMinimumQuantity);
        var lineId = cart.Items.Single().Id;
        foreach (var (quantity, total, unit) in new[] { (5, 45m, 9m), (10, 80m, 8m), (4, 40m, 10m), (10, 80m, 8m) })
        {
            var response = await owner.Client.PutAsJsonAsync($"/api/carts/{cart.Token}/items/{lineId}", new UpdateCartItemRequest(quantity));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); cart = (await response.Content.ReadFromJsonAsync<CartResponse>())!;
            Assert.Equal(total, cart.Subtotal.Amount); Assert.Equal(unit, cart.Items.Single().UnitPrice.Amount); Assert.Equal(10, cart.Items.Single().BaseUnitPrice.Amount);
        }
        var address = CheckoutQuoteApiTests.Address;
        var quoteResponse = await owner.Client.PostAsJsonAsync("/api/checkout/quote", new CheckoutQuoteRequest(cart.Token, owner.Email, address, "WELCOME10"));
        Assert.Equal(HttpStatusCode.OK, quoteResponse.StatusCode); var quote = (await quoteResponse.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;
        Assert.Equal(80, quote.Subtotal); Assert.Equal(8, quote.DiscountAmount); Assert.Equal(5.76m, quote.TaxAmount);
        Assert.Equal(8, quote.Lines.Single().UnitPrice); Assert.Equal(80, quote.Lines.Single().LineTotal);
        var checkout = await owner.Client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(cart.Token, owner.Email, address, "WELCOME10", "tok_visa"));
        Assert.True(checkout.IsSuccessStatusCode, await checkout.Content.ReadAsStringAsync()); var order = (await checkout.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal(quote.Total, order.Total); Assert.Equal(8, order.Items.Single().UnitPrice); Assert.Equal(80, order.Items.Single().LineTotal);
        (await scenario.Admin.PutAsJsonAsync(path, new PutQuantityPricingRequest(0, [new(5, 2), new(10, 1)]))).EnsureSuccessStatusCode();
        await scenario.Db(async db => { var stored = await db.Orders.SingleAsync(o => o.Number == order.Number); stored.MarkFulfilled(scenario.Clock.Instant); await db.SaveChangesAsync(); });
        var returned = await owner.Client.PostAsJsonAsync($"/api/orders/{order.Number}/returns", new CreateReturnRequestDto(null, "Damaged", null, [new(order.Items.Single().Id, 2)]));
        Assert.Equal(HttpStatusCode.Created, returned.StatusCode);
        // Historical unit 8 minus 10% coupon, plus 8% tax, for two units: 15.552 -> 15.55.
        Assert.Equal(15.55m, (await returned.Content.ReadFromJsonAsync<ReturnResponse>())!.RefundAmount);
    }

    [Fact]
    public async Task Saved_lines_are_priced_but_do_not_choose_active_currency_or_contribute_totals()
    {
        using var scenario = await ReportTestScenario.Create(); var cart = new Cart(); Guid savedId = default; Guid activeId = default;
        await scenario.Db(async db =>
        {
            var variants = await db.ProductVariants.Take(2).ToListAsync(); var saved = variants[0]; var active = variants[1];
            saved.Price = new Money(10, "EUR"); active.Price = new Money(10, "USD");
            savedId = cart.AddItem(saved.Id, 5).Id; cart.SaveForLater(savedId); activeId = cart.AddItem(active.Id, 4).Id;
            db.AddRange(cart, new VariantQuantityPricing(saved.Id, [new(5, 9)], 10)); await db.SaveChangesAsync();
        });
        using var client = scenario.App.CreateClient(); scenario.Commands.Statements.Clear();
        var result = (await client.GetFromJsonAsync<CartResponse>($"/api/carts/{cart.Token}"))!;
        Assert.Equal("USD", result.Subtotal.Currency); Assert.Equal(40, result.Subtotal.Amount);
        Assert.Equal("EUR", result.SavedItems.Single().UnitPrice.Currency); Assert.Equal(9, result.SavedItems.Single().UnitPrice.Amount);
        Assert.Single(scenario.Commands.Statements, sql => sql.Contains("FROM \"VariantQuantityTiers\""));
        var parked = (await (await client.PostAsync($"/api/carts/{cart.Token}/items/{activeId}/save-for-later", null)).Content.ReadFromJsonAsync<CartResponse>())!;
        Assert.Equal("USD", parked.Subtotal.Currency); Assert.Equal(0, parked.Subtotal.Amount);
        var activated = (await (await client.PostAsync($"/api/carts/{cart.Token}/items/{savedId}/activate", null)).Content.ReadFromJsonAsync<CartResponse>())!;
        Assert.Equal("EUR", activated.Subtotal.Currency); Assert.Equal(45, activated.Subtotal.Amount);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/carts/{cart.Token}/items/{activeId}/activate", null)).StatusCode);
        await scenario.Db(async db => Assert.True((await db.CartItems.SingleAsync(i => i.Id == activeId)).IsSavedForLater));
    }

    [Fact]
    public async Task Policy_replacement_checks_revision_precision_order_and_live_base_then_can_disable()
    {
        using var scenario = await ReportTestScenario.Create(); Guid id = default;
        await scenario.Db(async db => { var v = await db.ProductVariants.FirstAsync(); id = v.Id; v.Price = new Money(10); await db.SaveChangesAsync(); });
        var path = $"/api/admin/variants/{id}/quantity-pricing";
        Assert.Null((await scenario.Admin.GetFromJsonAsync<QuantityPricingResponse>(path))!.Revision);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PutAsJsonAsync(path, new { tiers = new[] { new QuantityTierInput(5, 9) } })).StatusCode);
        foreach (var tiers in new[] { new List<QuantityTierInput> { new(5, 9.001m) }, [new(5, 11)], [new(10, 8), new(5, 7)] })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await scenario.Admin.PutAsJsonAsync(path, new PutQuantityPricingRequest(null, tiers))).StatusCode);
        (await scenario.Admin.PutAsJsonAsync(path, new PutQuantityPricingRequest(null, [new(5, 9)]))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PutAsJsonAsync(path, new PutQuantityPricingRequest(null, []))).StatusCode);
        var cart = new Cart();
        await scenario.Db(async db => { var v = await db.ProductVariants.SingleAsync(v => v.Id == id); v.Price = new Money(7); cart.AddItem(id, 5); db.Carts.Add(cart); await db.SaveChangesAsync(); });
        Assert.Equal(35, (await scenario.Admin.GetFromJsonAsync<CartResponse>($"/api/carts/{cart.Token}"))!.Subtotal.Amount);
        (await scenario.Admin.PutAsJsonAsync(path, new PutQuantityPricingRequest(0, [new(2, 0)]))).EnsureSuccessStatusCode();
        Assert.Equal(0, (await scenario.Admin.GetFromJsonAsync<CartResponse>($"/api/carts/{cart.Token}"))!.Subtotal.Amount);
        (await scenario.Admin.PutAsJsonAsync(path, new PutQuantityPricingRequest(1, []))).EnsureSuccessStatusCode();
        Assert.Empty((await scenario.Admin.GetFromJsonAsync<QuantityPricingResponse>(path))!.Tiers);
        Assert.Equal(35, (await scenario.Admin.GetFromJsonAsync<CartResponse>($"/api/carts/{cart.Token}"))!.Subtotal.Amount);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PutAsJsonAsync(path, new PutQuantityPricingRequest(1, []))).StatusCode);
    }
}
