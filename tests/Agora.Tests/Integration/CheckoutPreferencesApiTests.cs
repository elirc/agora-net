using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class CheckoutPreferencesApiTests
{
    private const string Path = "/api/me/checkout-preferences";

    [Fact]
    public async Task Each_dimension_uses_explicit_then_saved_then_fallback_and_opt_out_preserves_existing_behavior()
    {
        using var scenario = await ReportTestScenario.Create(); using var owner = await AccountTestHelpers.Create(scenario, "preferences");
        var cart = new Cart { CustomerId = owner.Id };
        var address = new CustomerAddress { CustomerId = owner.Id, Label = "Germany", Address =
            new Address { FullName = "Saved name", Line1 = "Saved street", City = "Berlin", Region = "BE", PostalCode = "10115", Country = "DE" } };
        await scenario.Db(async db =>
        {
            var v = await db.ProductVariants.SingleAsync(v => v.Sku == "TEE-BLK-S"); v.Price = new Money(10); cart.AddItem(v.Id, 1);
            db.AddRange(cart, address, new ShippingMethod { Code = "preferred", Name = "Preferred", BaseRate = 17 }); await db.SaveChangesAsync();
        });
        var absent = (await owner.Client.GetFromJsonAsync<CheckoutPreferenceResponse>(Path))!;
        Assert.Equal(new CheckoutPreferenceResponse(null, null, null), absent);
        var save = await owner.Client.PutAsJsonAsync(Path, new PutCheckoutPreferenceRequest(address.Id, " PREFERRED ", null));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var preference = (await save.Content.ReadFromJsonAsync<CheckoutPreferenceResponse>())!;
        Assert.Equal(new CheckoutPreferenceResponse(address.Id, "preferred", 0), preference);
        var input = new CheckoutQuoteRequest(cart.Token, owner.Email, UseSavedPreferences: true);
        async Task Check(CheckoutQuoteRequest request, string method, decimal tax, decimal shipping)
        {
            var response = await owner.Client.PostAsJsonAsync("/api/checkout/quote", request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var quote = (await response.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;
            Assert.Equal(method, quote.ShippingMethodCode); Assert.Equal(tax, quote.TaxAmount); Assert.Equal(shipping, quote.ShippingAmount);
        }
        await Check(input, "preferred", 0, 17);
        await Check(input with { ShippingAddress = CheckoutQuoteApiTests.Address }, "preferred", .80m, 17);
        await Check(input with { ShippingMethodCode = "standard" }, "standard", 0, 5.99m);
        await Check(input with { ShippingAddress = CheckoutQuoteApiTests.Address, ShippingMethodCode = "standard" }, "standard", .80m, 5.99m);
        await Check(input with { UseSavedPreferences = false, ShippingAddress = CheckoutQuoteApiTests.Address }, "standard", .80m, 5.99m);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync("/api/checkout/quote", input with { UseSavedPreferences = false })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsJsonAsync("/api/checkout/quote", input with { ShippingMethodCode = "missing" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsJsonAsync("/api/checkout/quote", input with { ShippingMethodCode = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PostAsJsonAsync("/api/checkout/quote", input with { ShippingAddressId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PutAsJsonAsync(Path, new PutCheckoutPreferenceRequest(null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PutAsJsonAsync(Path, new PutCheckoutPreferenceRequest(null, null, 12))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PutAsJsonAsync(Path, new { shippingAddressId = address.Id, shippingMethodCode = "standard" })).StatusCode);
        var paid = await owner.Client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(cart.Token, owner.Email, null, null, "tok_visa", UseSavedPreferences: true));
        Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());
        var order = (await paid.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal("preferred", order.ShippingMethodCode); Assert.Equal("DE", order.ShippingAddress.Country); Assert.Equal(27m, order.Total);
        var cleared = await owner.Client.PutAsJsonAsync(Path, new PutCheckoutPreferenceRequest(null, null, 0));
        Assert.Equal(new CheckoutPreferenceResponse(null, null, 1), await cleared.Content.ReadFromJsonAsync<CheckoutPreferenceResponse>());
    }

    [Fact]
    public async Task Foreign_or_stale_references_are_revalidated_at_save_and_use_and_deleted_address_is_cleared()
    {
        using var scenario = await ReportTestScenario.Create(); using var owner = await AccountTestHelpers.Create(scenario, "preference-owner");
        using var other = await AccountTestHelpers.Create(scenario, "preference-other"); using var anonymous = scenario.App.CreateClient();
        var cart = new Cart { CustomerId = owner.Id };
        var mine = new CustomerAddress { CustomerId = owner.Id, Label = "Mine", Address = CheckoutQuoteApiTests.Address.ToAddress() };
        var theirs = new CustomerAddress { CustomerId = other.Id, Label = "Theirs", Address = CheckoutQuoteApiTests.Address.ToAddress() };
        await scenario.Db(async db =>
        {
            cart.AddItem((await db.ProductVariants.SingleAsync(v => v.Sku == "TEE-BLK-S")).Id, 1);
            db.AddRange(cart, mine, theirs, new ShippingMethod { Code = "stale-method", Name = "Stale", BaseRate = 7 }); await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(Path, new PutCheckoutPreferenceRequest(null, null, null))).StatusCode);
        var input = new CheckoutQuoteRequest(cart.Token, owner.Email, UseSavedPreferences: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/checkout/quote", input)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/checkout", new CheckoutRequest(cart.Token, owner.Email, null, null, "tok_visa", UseSavedPreferences: true))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PutAsJsonAsync(Path, new PutCheckoutPreferenceRequest(theirs.Id, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PutAsJsonAsync(Path, new PutCheckoutPreferenceRequest(mine.Id, "missing", null))).StatusCode);
        (await owner.Client.PutAsJsonAsync(Path, new PutCheckoutPreferenceRequest(mine.Id, "stale-method", null))).EnsureSuccessStatusCode();
        Assert.Null((await other.Client.GetFromJsonAsync<CheckoutPreferenceResponse>(Path))!.Version);
        await scenario.Db(async db => { (await db.ShippingMethods.SingleAsync(m => m.Code == "stale-method")).IsActive = false; await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsJsonAsync("/api/checkout/quote", input)).StatusCode);
        (await owner.Client.PostAsJsonAsync("/api/checkout/quote", input with { ShippingMethodCode = "standard" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PutAsJsonAsync(Path, new PutCheckoutPreferenceRequest(mine.Id, "stale-method", 0))).StatusCode);
        await scenario.Db(async db =>
        {
            db.ShippingMethods.Remove(await db.ShippingMethods.SingleAsync(m => m.Code == "stale-method"));
            // A corrupt cross-owner reference still satisfies the FK: use-time ownership must catch it.
            (await db.CustomerAddresses.SingleAsync(a => a.Id == mine.Id)).CustomerId = other.Id;
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PostAsJsonAsync("/api/checkout/quote", input with { ShippingMethodCode = "standard" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsJsonAsync("/api/checkout/quote", input with { ShippingAddress = CheckoutQuoteApiTests.Address })).StatusCode);
        await scenario.Db(async db => { db.CustomerAddresses.Remove(await db.CustomerAddresses.SingleAsync(a => a.Id == mine.Id)); await db.SaveChangesAsync(); });
        Assert.Null((await owner.Client.GetFromJsonAsync<CheckoutPreferenceResponse>(Path))!.ShippingAddressId);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync("/api/checkout/quote", input with { ShippingMethodCode = "standard" })).StatusCode);
        (await owner.Client.PutAsJsonAsync(Path, new PutCheckoutPreferenceRequest(null, null, 0))).EnsureSuccessStatusCode();
        (await owner.Client.PostAsJsonAsync("/api/checkout/quote", input with { ShippingAddress = CheckoutQuoteApiTests.Address })).EnsureSuccessStatusCode();
        await scenario.Db(async db => { Assert.Empty(await db.Orders.ToListAsync()); Assert.Equal(cart.Version, (await db.Carts.SingleAsync(c => c.Id == cart.Id)).Version); });
    }
}
