using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class DiscountSchedulesApiTests
{
    [Fact]
    public async Task Shared_captured_clock_enforces_inclusive_start_exclusive_expiry_before_checkout_mutations()
    {
        var providers = new CountingCheckoutProviders(); using var scenario = await ReportTestScenario.Create(providers.Register);
        using var client = scenario.App.CreateClient();
        var start = scenario.Clock.Instant.AddHours(1); var end = start.AddHours(1);
        var create = await scenario.Admin.PostAsJsonAsync("/api/discounts", new CreateDiscountRequest("SCHEDULED", "Percentage", 10, null, end, null, true, start));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(start, (await create.Content.ReadFromJsonAsync<DiscountResponse>())!.StartsAt);
        var cart = new Cart(); var later = new Cart(); Guid variantId = default; int stock = 0;
        await scenario.Db(async db =>
        {
            var v = await db.ProductVariants.Include(v => v.Inventory).SingleAsync(v => v.Sku == "TEE-BLK-S");
            variantId = v.Id; stock = v.Inventory!.QuantityOnHand; cart.AddItem(v.Id, 1); later.AddItem(v.Id, 1);
            db.Carts.AddRange(cart, later); await db.SaveChangesAsync();
        });
        var quote = new CheckoutQuoteRequest(cart.Token, "scheduled@example.test", CheckoutQuoteApiTests.Address, "SCHEDULED");
        var checkout = new CheckoutRequest(cart.Token, quote.Email, quote.ShippingAddress, quote.DiscountCode, "tok_visa");
        scenario.Clock.Instant = start.AddTicks(-1);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/checkout/quote", quote)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/checkout", checkout)).StatusCode);
        Assert.Equal(0, providers.Charges);
        await scenario.Db(async db =>
        {
            Assert.Empty(await db.Orders.ToListAsync()); Assert.Equal(0, (await db.DiscountCodes.SingleAsync(d => d.Code == "SCHEDULED")).TimesUsed);
            var inventory = await db.InventoryItems.SingleAsync(i => i.ProductVariantId == variantId);
            Assert.Equal((stock, 0), (inventory.QuantityOnHand, inventory.QuantityReserved));
            Assert.Equal(cart.Version, (await db.Carts.SingleAsync(c => c.Id == cart.Id)).Version);
        });
        scenario.Clock.Instant = start;
        var priced = (await (await client.PostAsJsonAsync("/api/checkout/quote", quote)).Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;
        Assert.Equal(start, priced.CalculatedAt); Assert.Equal(2m, priced.DiscountAmount);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/checkout", checkout)).StatusCode);
        Assert.Equal(1, providers.Charges);
        scenario.Clock.Instant = end;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/checkout/quote", quote with { CartToken = later.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/checkout", checkout with { CartToken = later.Token })).StatusCode);
        await scenario.Db(async db => Assert.Equal(1, (await db.DiscountCodes.SingleAsync(d => d.Code == "SCHEDULED")).TimesUsed));
    }

    [Fact]
    public async Task Schedule_validates_final_pair_accepts_equivalent_offsets_and_replacement_clears_omitted_start()
    {
        using var scenario = await ReportTestScenario.Create(); var start = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        foreach (var expiry in new[] { start, start.AddTicks(-1) })
            Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync("/api/discounts",
                new CreateDiscountRequest("INVALID", "FixedAmount", 1, null, expiry, null, true, start))).StatusCode);
        var created = await scenario.Admin.PostAsJsonAsync("/api/discounts", new CreateDiscountRequest("OFFSET", "FixedAmount", 1, null,
            start.AddHours(1).ToOffset(TimeSpan.FromHours(-7)), null, true, start.ToOffset(TimeSpan.FromHours(5.5))));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var response = (await created.Content.ReadFromJsonAsync<DiscountResponse>())!;
        Assert.Equal(start, response.StartsAt); Assert.Equal(start.AddHours(1), response.ExpiresAt);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PutAsJsonAsync("/api/discounts/OFFSET", new UpdateDiscountRequest(start, null, true, start))).StatusCode);
        var unchanged = (await scenario.Admin.GetFromJsonAsync<DiscountResponse>("/api/discounts/OFFSET"))!;
        Assert.Equal(start.AddHours(1), unchanged.ExpiresAt);
        var cleared = await scenario.Admin.PutAsJsonAsync("/api/discounts/OFFSET", new { expiresAt = start.AddHours(2), usageLimit = (int?)null, isActive = true });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode); Assert.Null((await cleared.Content.ReadFromJsonAsync<DiscountResponse>())!.StartsAt);
        var invalidLocal = await scenario.Admin.PostAsJsonAsync("/api/discounts", new { code = "LOCAL", type = "FixedAmount", value = 1,
            startsAt = "2030-01-01T12:00:00", expiresAt = "2030-01-01T13:00:00Z" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidLocal.StatusCode);
        var legacy = await scenario.Admin.PostAsJsonAsync("/api/discounts", new CreateDiscountRequest("NO-START", "FixedAmount", 1, null, null, null, true));
        Assert.Equal(HttpStatusCode.Created, legacy.StatusCode); Assert.Null((await legacy.Content.ReadFromJsonAsync<DiscountResponse>())!.StartsAt);
    }
}
