using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Agora.Tests.Integration;

public class CheckoutQuoteApiTests
{
    internal static readonly AddressDto Address = new("Quote reader", "1 Quote Lane", null, "Town", "VA", "22201", "US");

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    public async Task Repeated_quotes_do_not_write_or_call_providers_and_equal_immediate_checkout(decimal giftAmount)
    {
        var providers = new CountingCheckoutProviders();
        using var scenario = await ReportTestScenario.Create(providers.Register);
        using var client = scenario.App.CreateClient();
        var cart = new Cart(); var gift = new GiftCard(giftAmount); Guid id = default; int stock = 0;
        await scenario.Db(async db =>
        {
            var variant = await db.ProductVariants.Include(v => v.Inventory).SingleAsync(v => v.Sku == "TEE-BLK-S");
            id = variant.Id; stock = variant.Inventory!.QuantityOnHand;
            cart.AddItem(id, 2);
            var saved = await db.ProductVariants.FirstAsync(v => v.Id != id);
            cart.SaveForLater(cart.AddItem(saved.Id, 1).Id);
            db.AddRange(cart, gift, new WebhookSubscription { Url = "https://example.test/hook", Secret = "test-only-secret", Events = [WebhookEvents.OrderCreated, WebhookEvents.OrderPaid] });
            await db.SaveChangesAsync();
        });
        int uses = 0;
        await scenario.Db(async db => uses = (await db.DiscountCodes.SingleAsync(d => d.Code == "WELCOME10")).TimesUsed);
        scenario.Commands.Statements.Clear();
        var input = new CheckoutQuoteRequest(cart.Token, "quote@example.test", Address, "WELCOME10", GiftCardCode: gift.Code);
        CheckoutQuoteResponse quote = null!;
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/checkout/quote", input);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.True(response.Headers.CacheControl!.NoStore);
            quote = (await response.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;
            Assert.Equal(scenario.Clock.Instant, quote.CalculatedAt); Assert.Equal(cart.Version, quote.CartVersion);
            Assert.Single(quote.Lines); Assert.Equal(2, quote.Lines[0].Quantity);
            Assert.Equal(39.98m, quote.Subtotal); Assert.Equal(4m, quote.DiscountAmount);
            Assert.Equal(2.88m, quote.TaxAmount); Assert.Equal(5.99m, quote.ShippingAmount); Assert.Equal(44.85m, quote.Total);
            Assert.Equal(Math.Min(giftAmount, quote.Total), quote.GiftCardAmount);
            Assert.Equal(quote.Total - quote.GiftCardAmount, quote.RemainingPayable);
        }
        Assert.DoesNotContain(scenario.Commands.Statements, sql => sql.Contains("INSERT INTO") || sql.Contains("UPDATE ") || sql.Contains("DELETE FROM"));
        Assert.Equal((0, 0, 0), (providers.Charges, providers.Refunds, providers.Sends));
        await scenario.Db(async db =>
        {
            Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.WebhookDeliveries.ToListAsync());
            var current = await db.Carts.Include(c => c.Items).SingleAsync(c => c.Id == cart.Id);
            Assert.Equal(cart.Version, current.Version); Assert.Equal(2, current.Items.Count);
            var inventory = await db.InventoryItems.SingleAsync(i => i.ProductVariantId == id);
            Assert.Equal(stock, inventory.QuantityOnHand); Assert.Equal(0, inventory.QuantityReserved);
            Assert.Equal(uses, (await db.DiscountCodes.SingleAsync(d => d.Code == "WELCOME10")).TimesUsed);
            var currentGift = await db.GiftCards.SingleAsync(g => g.Id == gift.Id);
            Assert.Equal(giftAmount, currentGift.Balance); Assert.Equal(0, currentGift.Version);
        });
        var checkout = await client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(cart.Token, input.Email, Address, input.DiscountCode,
            "tok_visa", GiftCardCode: gift.Code));
        Assert.True(checkout.IsSuccessStatusCode, await checkout.Content.ReadAsStringAsync());
        var order = (await checkout.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal((quote.Subtotal, quote.DiscountAmount, quote.TaxAmount, quote.ShippingAmount, quote.Total, quote.GiftCardAmount),
            (order.Subtotal, order.DiscountAmount, order.TaxAmount, order.ShippingAmount, order.Total, order.GiftCardAmount));
        Assert.Equal(giftAmount < quote.Total ? 1 : 0, providers.Charges);
        Assert.Equal(0, providers.Sends); // Checkout commits intent; the worker transports it later.
        await scenario.Db(async db =>
        {
            Assert.Equal(2, await db.Set<OutboxEvent>().CountAsync());
            var deliveries = await db.WebhookDeliveries.ToArrayAsync();
            Assert.Equal(2, deliveries.Length);
            Assert.All(deliveries, delivery => Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status));
        });
    }

    [Fact]
    public async Task Quote_is_nonbinding_and_checkout_recalculates_changed_price()
    {
        using var scenario = await ReportTestScenario.Create(); using var client = scenario.App.CreateClient();
        var cart = new Cart(); Guid id = default;
        await scenario.Db(async db =>
        {
            var v = await db.ProductVariants.FirstAsync(v => v.Sku == "TEE-BLK-S"); id = v.Id; v.Price = new Money(10);
            cart.AddItem(id, 1); db.Carts.Add(cart); await db.SaveChangesAsync();
        });
        var quote = (await (await client.PostAsJsonAsync("/api/checkout/quote", new CheckoutQuoteRequest(cart.Token, "quote@example.test", Address)))
            .Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;
        Assert.Equal(10m, quote.Subtotal);
        await scenario.Db(async db => { (await db.ProductVariants.SingleAsync(v => v.Id == id)).Price = new Money(12); await db.SaveChangesAsync(); });
        var order = (await (await client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(cart.Token, "quote@example.test", Address, null, "tok_visa")))
            .Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal(12m, order.Subtotal); Assert.NotEqual(quote.Total, order.Total);
    }

    [Fact]
    public async Task Invalid_selection_and_stock_failures_match_checkout_without_side_effects()
    {
        var providers = new CountingCheckoutProviders(); using var scenario = await ReportTestScenario.Create(providers.Register);
        using var owner = await AccountTestHelpers.Create(scenario, "quote-owner"); using var other = await AccountTestHelpers.Create(scenario, "quote-other");
        var cart = new Cart { CustomerId = owner.Id }; Guid variantId = default; Guid foreignAddress = default;
        var expiredGift = new GiftCard(10, expiresAt: scenario.Clock.Instant);
        await scenario.Db(async db =>
        {
            var variant = await db.ProductVariants.SingleAsync(v => v.Sku == "TEE-BLK-S"); variantId = variant.Id;
            cart.AddItem(variant.Id, 1);
            var address = new CustomerAddress { CustomerId = other.Id, Label = "Foreign", Address = Address.ToAddress() }; foreignAddress = address.Id;
            db.AddRange(cart, address, expiredGift, new DiscountCode { Code = "EXPIRED-QUOTE", Type = DiscountType.FixedAmount, Value = 1, ExpiresAt = scenario.Clock.Instant });
            await db.SaveChangesAsync();
        });
        var basic = new CheckoutQuoteRequest(cart.Token, owner.Email, Address);
        async Task Both(CheckoutQuoteRequest request, HttpStatusCode status)
        {
            Assert.Equal(status, (await owner.Client.PostAsJsonAsync("/api/checkout/quote", request)).StatusCode);
            Assert.Equal(status, (await owner.Client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(request.CartToken, request.Email,
                request.ShippingAddress, request.DiscountCode, "tok_visa", request.ShippingMethodCode, request.ShippingAddressId, request.GiftCardCode))).StatusCode);
        }
        await Both(basic with { ShippingAddressId = foreignAddress }, HttpStatusCode.NotFound);
        await Both(basic with { ShippingMethodCode = "missing" }, HttpStatusCode.UnprocessableEntity);
        await Both(basic with { DiscountCode = "EXPIRED-QUOTE" }, HttpStatusCode.UnprocessableEntity);
        await Both(basic with { GiftCardCode = expiredGift.Code }, HttpStatusCode.UnprocessableEntity);
        await scenario.Db(async db => { (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == variantId)).SetStock(0); await db.SaveChangesAsync(); });
        await Both(basic, HttpStatusCode.Conflict);
        Assert.Equal((0, 0, 0), (providers.Charges, providers.Refunds, providers.Sends));
        await scenario.Db(async db =>
        {
            Assert.Empty(await db.Orders.ToListAsync()); Assert.Equal(cart.Version, (await db.Carts.SingleAsync(c => c.Id == cart.Id)).Version);
            Assert.Equal(0, (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == variantId)).QuantityReserved);
            Assert.Equal(0, (await db.DiscountCodes.SingleAsync(d => d.Code == "EXPIRED-QUOTE")).TimesUsed);
            Assert.Equal(10m, (await db.GiftCards.SingleAsync(g => g.Id == expiredGift.Id)).Balance);
        });
    }

    [Fact]
    public async Task Weight_is_widened_before_multiplication_and_summing()
    {
        using var scenario = await ReportTestScenario.Create(); using var client = scenario.App.CreateClient(); var cart = new Cart();
        await scenario.Db(async db =>
        {
            var product = await db.Products.FirstAsync(p => p.IsActive);
            for (var i = 0; i < 30; i++)
            {
                var v = new ProductVariant { ProductId = product.Id, Sku = "HEAVY-" + i, Name = "Heavy", Price = new Money(1), WeightGrams = 1_000_000 };
                db.AddRange(v, new InventoryItem(v.Id, 100)); cart.AddItem(v.Id, 99);
            }
            db.ShippingMethods.Add(new ShippingMethod { Code = "weight-quote", Name = "Weight", RateType = ShippingRateType.Weighted, BaseRate = 0, PerKgRate = 1 });
            db.Carts.Add(cart); await db.SaveChangesAsync();
        });
        var response = await client.PostAsJsonAsync("/api/checkout/quote", new CheckoutQuoteRequest(cart.Token, "weight@example.test", Address, ShippingMethodCode: "weight-quote"));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var quote = (await response.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;
        Assert.Equal(2_970_000_000L, quote.TotalWeightGrams); Assert.Equal(2_970_000m, quote.ShippingAmount);
    }
}

internal sealed class CountingCheckoutProviders : IPaymentGateway, IWebhookSender
{
    public int Charges; public int Refunds; public int Sends;
    public void Register(IServiceCollection services)
    {
        services.RemoveAll<IPaymentGateway>(); services.AddSingleton<IPaymentGateway>(this);
        services.RemoveAll<IWebhookSender>(); services.AddSingleton<IWebhookSender>(this);
    }
    public Task<PaymentResult> ChargeAsync(string orderNumber, Money amount, string paymentToken, CancellationToken ct = default)
    { Interlocked.Increment(ref Charges); return Task.FromResult(PaymentResult.Succeeded("quote-test-transaction")); }
    public Task<PaymentResult> RefundAsync(string transactionId, Money amount, CancellationToken ct = default)
    { Interlocked.Increment(ref Refunds); return Task.FromResult(PaymentResult.Succeeded("quote-test-refund")); }
    public Task<WebhookSendResult> SendAsync(string url, string payload, string signature, CancellationToken ct = default)
    { Interlocked.Increment(ref Sends); return Task.FromResult(new WebhookSendResult(true, 200)); }
}
