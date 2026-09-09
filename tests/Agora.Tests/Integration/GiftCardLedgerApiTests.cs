using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class GiftCardLedgerApiTests
{
    private static readonly AddressDto Address = new("Ledger learner", "1 Ledger Way", null, "Berlin", "BE", "10115", "DE");

    [Fact]
    public async Task Issue_redeem_and_return_credit_explain_fifty_thirty_thirty_five_without_exposing_bearer_code()
    {
        var providers = new CountingCheckoutProviders(); using var scenario = await ReportTestScenario.Create(providers.Register);
        using var owner = await AccountTestHelpers.Create(scenario, "ledger-owner");
        var issued = await scenario.Admin.PostAsJsonAsync("/api/gift-cards", new IssueGiftCardRequest(50, null, null));
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var card = (await issued.Content.ReadFromJsonAsync<GiftCardResponse>())!;
        Assert.NotEqual(Guid.Empty, card.Id);
        Guid cardId = default; Guid orderId = default; Guid returnId = default;
        var cart = new Cart { CustomerId = owner.Id };
        await scenario.Db(async db =>
        {
            cardId = (await db.GiftCards.SingleAsync(g => g.Code == card.Code)).Id;
            Assert.Equal(card.Id, cardId);
            var v = await db.ProductVariants.SingleAsync(v => v.Sku == "TEE-BLK-S"); v.Price = new Money(5); cart.AddItem(v.Id, 4);
            db.AddRange(cart, new ShippingMethod { Code = "ledger-pickup", Name = "Pickup", BaseRate = 0 }); await db.SaveChangesAsync();
        });
        var paid = await owner.Client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(cart.Token, owner.Email, Address, null, "tok_visa", "ledger-pickup", GiftCardCode: card.Code));
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        var order = (await paid.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal(20m, order.GiftCardAmount);
        (await scenario.Admin.PostAsync($"/api/orders/{order.Number}/fulfill", null)).EnsureSuccessStatusCode();
        (await scenario.Admin.PostAsync($"/api/gift-cards/{card.Code}/deactivate", null)).EnsureSuccessStatusCode();
        var requested = await owner.Client.PostAsJsonAsync($"/api/orders/{order.Number}/returns", new CreateReturnRequestDto(null, "Damaged", null, [new(order.Items.Single().Id, 1)]));
        Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
        var rma = (await requested.Content.ReadFromJsonAsync<ReturnResponse>())!;
        (await scenario.Admin.PostAsync($"/api/returns/{rma.Number}/approve", null)).EnsureSuccessStatusCode();
        await scenario.Db(async db =>
        {
            orderId = (await db.Orders.SingleAsync(o => o.Number == order.Number)).Id; returnId = (await db.ReturnRequests.SingleAsync(r => r.Number == rma.Number)).Id;
            var current = await db.GiftCards.SingleAsync(g => g.Id == cardId); Assert.Equal(35m, current.Balance); Assert.False(current.IsActive);
        });
        scenario.Commands.Statements.Clear();
        var path = $"/api/admin/gift-cards/{cardId}/transactions";
        var response = await scenario.Admin.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(card.Code, body);
        var ledger = (await response.Content.ReadFromJsonAsync<GiftCardLedgerResponse>())!;
        Assert.Equal("Issued", ledger.HistoryStartsWith); Assert.Equal(0, ledger.OpeningRecordedVersion);
        Assert.Equal(new[] { "Issued", "Redeemed", "RefundCredit" }, ledger.Entries.Items.Select(e => e.Kind));
        Assert.Equal(new[] { 50m, -20m, 5m }, ledger.Entries.Items.Select(e => e.Amount));
        Assert.Equal(new[] { 50m, 30m, 35m }, ledger.Entries.Items.Select(e => e.BalanceAfter));
        Assert.Equal(new[] { 0, 1, 2 }, ledger.Entries.Items.Select(e => e.RecordedVersion));
        Assert.All(ledger.Entries.Items, e => { Assert.Equal(cardId, e.GiftCardId); Assert.Equal(scenario.Clock.Instant, e.RecordedAt); });
        Assert.Equal(orderId, ledger.Entries.Items[1].SourceOrderId); Assert.Null(ledger.Entries.Items[1].SourceReturnId);
        Assert.Equal(returnId, ledger.Entries.Items[2].SourceReturnId); Assert.Equal(orderId, ledger.Entries.Items[2].SourceOrderId);
        Assert.DoesNotContain(scenario.Commands.Statements, sql => sql.Contains("\"Code\"") || sql.Contains("INSERT INTO") || sql.Contains("UPDATE ") || sql.Contains("DELETE FROM"));
        var secondPage = (await scenario.Admin.GetFromJsonAsync<GiftCardLedgerResponse>(path + "?page=2&pageSize=1"))!;
        Assert.Equal(3, secondPage.Entries.TotalCount); Assert.Equal(1, secondPage.Entries.Items.Single().RecordedVersion);
        Assert.Equal("Issued", secondPage.HistoryStartsWith);
        Assert.Equal((0, 0, 0), (providers.Charges, providers.Refunds, providers.Sends));
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.GetAsync(path)).StatusCode);
        using var anonymous = scenario.App.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.GetAsync($"/api/admin/gift-cards/{Guid.NewGuid()}/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.GetAsync(path + "?page=2147483647&pageSize=100")).StatusCode);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("refund")]
    public async Task Full_order_tender_credit_is_recorded_once_with_its_order_source(string action)
    {
        var providers = new CountingCheckoutProviders(); using var scenario = await ReportTestScenario.Create(providers.Register);
        using var owner = await AccountTestHelpers.Create(scenario, "ledger-" + action);
        var card = (await (await scenario.Admin.PostAsJsonAsync("/api/gift-cards", new IssueGiftCardRequest(10, null, null))).Content.ReadFromJsonAsync<GiftCardResponse>())!;
        var cart = new Cart { CustomerId = owner.Id }; Guid cardId = default;
        await scenario.Db(async db =>
        {
            cardId = (await db.GiftCards.SingleAsync(g => g.Code == card.Code)).Id;
            var v = await db.ProductVariants.SingleAsync(v => v.Sku == "TEE-BLK-S"); v.Price = new Money(20); cart.AddItem(v.Id, 1);
            db.AddRange(cart, new ShippingMethod { Code = "ledger-pickup", Name = "Pickup", BaseRate = 0 }); await db.SaveChangesAsync();
        });
        var order = (await (await owner.Client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(cart.Token, owner.Email, Address, null, "tok_visa", "ledger-pickup", GiftCardCode: card.Code)))
            .Content.ReadFromJsonAsync<OrderResponse>())!;
        var actor = action == "refund" ? scenario.Admin : owner.Client;
        Assert.Equal(HttpStatusCode.OK, (await actor.PostAsync($"/api/orders/{order.Number}/{action}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await actor.PostAsync($"/api/orders/{order.Number}/{action}", null)).StatusCode);
        var ledger = (await scenario.Admin.GetFromJsonAsync<GiftCardLedgerResponse>($"/api/admin/gift-cards/{cardId}/transactions"))!;
        Assert.Equal(new[] { 10m, -10m, 10m }, ledger.Entries.Items.Select(e => e.Amount));
        Assert.Equal(10m, ledger.Entries.Items.Last().BalanceAfter); Assert.Null(ledger.Entries.Items.Last().SourceReturnId);
        Assert.Equal((1, 1), (providers.Charges, providers.Refunds));
    }

    [Fact]
    public async Task Invalid_redemption_and_zero_gift_tender_add_no_monetary_entries()
    {
        using var scenario = await ReportTestScenario.Create(); using var owner = await AccountTestHelpers.Create(scenario, "ledger-zero");
        var card = (await (await scenario.Admin.PostAsJsonAsync("/api/gift-cards", new IssueGiftCardRequest(10, null, null))).Content.ReadFromJsonAsync<GiftCardResponse>())!;
        var cart = new Cart { CustomerId = owner.Id }; Guid cardId = default;
        await scenario.Db(async db =>
        {
            cardId = (await db.GiftCards.SingleAsync(g => g.Code == card.Code)).Id;
            cart.AddItem((await db.ProductVariants.SingleAsync(v => v.Sku == "TEE-BLK-S")).Id, 1);
            db.AddRange(cart, new ShippingMethod { Code = "ledger-free", Name = "Free", BaseRate = 0 },
                new DiscountCode { Code = "LEDGER-FREE", Type = DiscountType.Percentage, Value = 100 }); await db.SaveChangesAsync();
        });
        var input = new CheckoutRequest(cart.Token, owner.Email, Address, "LEDGER-FREE", "tok_fail", "ledger-free", GiftCardCode: card.Code);
        var result = await owner.Client.PostAsJsonAsync("/api/checkout", input); Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.Equal(0m, (await result.Content.ReadFromJsonAsync<OrderResponse>())!.GiftCardAmount);
        (await scenario.Admin.PostAsync($"/api/gift-cards/{card.Code}/deactivate", null)).EnsureSuccessStatusCode();
        var next = new Cart { CustomerId = owner.Id };
        await scenario.Db(async db => { next.AddItem((await db.ProductVariants.SingleAsync(v => v.Sku == "TEE-BLK-S")).Id, 1); db.Carts.Add(next); await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsJsonAsync("/api/checkout", input with { CartToken = next.Token, DiscountCode = null })).StatusCode);
        var ledger = (await scenario.Admin.GetFromJsonAsync<GiftCardLedgerResponse>($"/api/admin/gift-cards/{cardId}/transactions"))!;
        Assert.Single(ledger.Entries.Items); Assert.Equal("Issued", ledger.Entries.Items.Single().Kind);
    }
}
