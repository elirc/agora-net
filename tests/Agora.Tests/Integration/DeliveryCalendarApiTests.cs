using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class DeliveryCalendarApiTests
{
    private static readonly AddressDto Address = new("Calendar buyer", "1 Date Way", null, "Town", "CA", "90001", "US");

    [Theory]
    [InlineData(13, 59, "2026-09-11", "2026-09-15")]
    [InlineData(14, 0, "2026-09-15", "2026-09-16")]
    public async Task Quote_and_checkout_share_exact_cutoff_and_business_day_estimates(int hour, int minute, string from, string to)
    {
        using var scenario = await ReportTestScenario.Create(); scenario.Clock.Instant = new DateTimeOffset(2026, 9, 11, hour, minute, 0, TimeSpan.Zero);
        var initial = (await scenario.Admin.GetFromJsonAsync<DeliveryCalendarResponse>("/api/admin/delivery-calendar"))!;
        Assert.False(initial.Enabled); Assert.Equal(0, initial.Revision);
        var update = await scenario.Admin.PutAsJsonAsync("/api/admin/delivery-calendar",
            new PutDeliveryCalendarRequest(true, "14:00", [new DateOnly(2026, 9, 14)], initial.Revision));
        update.EnsureSuccessStatusCode();
        var cart = new Cart();
        await scenario.Db(async db =>
        {
            var oldDefaults = await db.ShippingMethods.Where(m => m.IsDefault).ToListAsync(); foreach (var oldMethod in oldDefaults) oldMethod.IsDefault = false;
            var calendarMethod = new ShippingMethod { Code = "calendar", Name = "Calendar", IsActive = true, IsDefault = true, MinDays = 0, MaxDays = 1 };
            var variant = await db.ProductVariants.SingleAsync(v => v.Sku == "TEE-BLK-S"); cart.AddItem(variant.Id, 1); db.AddRange(calendarMethod, cart); await db.SaveChangesAsync();
        });
        using var shopper = scenario.App.CreateClient();
        var quote = (await (await shopper.PostAsJsonAsync("/api/checkout/quote", new CheckoutQuoteRequest(cart.Token, "calendar@example.test", Address)))
            .Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;
        Assert.Equal(DateOnly.Parse(from), DateOnly.FromDateTime(quote.EstimatedDeliveryFrom.UtcDateTime));
        Assert.Equal(DateOnly.Parse(to), DateOnly.FromDateTime(quote.EstimatedDeliveryTo.UtcDateTime));
        Assert.Equal(TimeSpan.Zero, quote.EstimatedDeliveryFrom.TimeOfDay);
        var checkout = await shopper.PostAsJsonAsync("/api/checkout", new CheckoutRequest(cart.Token, "calendar@example.test", Address, null, "tok_visa"));
        checkout.EnsureSuccessStatusCode(); var order = (await checkout.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal(quote.EstimatedDeliveryFrom, order.EstimatedDeliveryFrom); Assert.Equal(quote.EstimatedDeliveryTo, order.EstimatedDeliveryTo);
    }

    [Fact]
    public async Task Replacement_rejects_stale_revision_duplicate_dates_and_non_minute_cutoffs()
    {
        using var scenario = await ReportTestScenario.Create();
        var first = await scenario.Admin.PutAsJsonAsync("/api/admin/delivery-calendar",
            new PutDeliveryCalendarRequest(true, "09:30", [new DateOnly(2026, 12, 25)], 0));
        first.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PutAsJsonAsync("/api/admin/delivery-calendar",
            new PutDeliveryCalendarRequest(false, "09:30", [], 0))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await scenario.Admin.PutAsJsonAsync("/api/admin/delivery-calendar",
            new PutDeliveryCalendarRequest(true, "09:30", [new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 25)], 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PutAsJsonAsync("/api/admin/delivery-calendar",
            new { enabled = true, cutoffUtc = "09:30:01", closureDates = Array.Empty<string>(), expectedRevision = 1 })).StatusCode);
    }
}
