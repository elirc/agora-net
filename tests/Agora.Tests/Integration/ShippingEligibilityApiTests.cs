using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class ShippingEligibilityApiTests
{
    private static readonly AddressDto Us = new("Buyer", "1 Main", null, "Town", "CA", "90001", "US");

    [Fact]
    public async Task Preview_is_informational_and_checkout_recomputes_trusted_cart_weight_before_side_effects()
    {
        var providers = new CountingCheckoutProviders(); using var scenario = await ReportTestScenario.Create(providers.Register);
        Guid methodId = default; var cart = new Cart(); Guid inventoryId = default; int available = 0;
        await scenario.Db(async db =>
        {
            var method = new ShippingMethod { Code = "light", Name = "Light", BaseRate = 1, MinDays = 1, MaxDays = 2, IsActive = true };
            var variant = await db.ProductVariants.Include(v => v.Inventory).SingleAsync(v => v.Sku == "TEE-BLK-S");
            cart.AddItem(variant.Id, 2); db.AddRange(method, cart); await db.SaveChangesAsync();
            methodId = method.Id; inventoryId = variant.Id; available = variant.Inventory!.QuantityAvailable;
        });
        var policy = await scenario.Admin.PutAsJsonAsync($"/api/admin/shipping-methods/{methodId}/eligibility",
            new PutShippingEligibilityRequest([" ca ", "us"], 200, null));
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);
        var normalized = (await policy.Content.ReadFromJsonAsync<ShippingEligibilityPolicyResponse>())!;
        Assert.Equal(["CA", "US"], normalized.Countries); Assert.Equal(0, normalized.Revision);

        using var shopper = scenario.App.CreateClient();
        var preview = (await (await shopper.PostAsJsonAsync("/api/shipping-methods/eligibility",
            new ShippingEligibilityPreviewRequest("us", 100))).Content.ReadFromJsonAsync<EligibleShippingMethodResponse[]>())!;
        Assert.Contains(preview, m => m.Code == "light");
        var rejected = await shopper.PostAsJsonAsync("/api/checkout", new CheckoutRequest(cart.Token, "buyer@example.test", Us, null, "tok_visa", "light"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Contains("WeightExceeded", await rejected.Content.ReadAsStringAsync());
        Assert.Equal((0, 0, 0), (providers.Charges, providers.Refunds, providers.Sends));
        await scenario.Db(async db =>
        {
            Assert.Empty(await db.Orders.ToListAsync());
            Assert.Equal(available, (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == inventoryId)).QuantityAvailable);
        });
    }

    [Fact]
    public async Task Policy_replacement_uses_exact_revision_and_unconfigured_or_inactive_methods_have_clear_behavior()
    {
        using var scenario = await ReportTestScenario.Create(); Guid active = default; Guid inactive = default;
        await scenario.Db(async db =>
        {
            var a = new ShippingMethod { Code = "open-anywhere", Name = "Open", IsActive = true };
            var i = new ShippingMethod { Code = "hidden-policy", Name = "Hidden", IsActive = false };
            db.AddRange(a, i); await db.SaveChangesAsync(); active = a.Id; inactive = i.Id;
        });
        var absent = (await scenario.Admin.GetFromJsonAsync<ShippingEligibilityPolicyResponse>($"/api/admin/shipping-methods/{active}/eligibility"))!;
        Assert.Null(absent.Revision); Assert.Empty(absent.Countries);
        (await scenario.Admin.PutAsJsonAsync($"/api/admin/shipping-methods/{active}/eligibility",
            new PutShippingEligibilityRequest([], null, null))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PutAsJsonAsync($"/api/admin/shipping-methods/{active}/eligibility",
            new PutShippingEligibilityRequest(["US"], 1, null))).StatusCode);
        using var anonymous = scenario.App.CreateClient();
        var methods = (await (await anonymous.PostAsJsonAsync("/api/shipping-methods/eligibility",
            new ShippingEligibilityPreviewRequest("GB", long.MaxValue))).Content.ReadFromJsonAsync<EligibleShippingMethodResponse[]>())!;
        Assert.Contains(methods, m => m.Id == active); Assert.DoesNotContain(methods, m => m.Id == inactive);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/admin/shipping-methods/{active}/eligibility")).StatusCode);
    }
}
