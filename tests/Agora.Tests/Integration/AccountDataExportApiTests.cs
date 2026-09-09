using System.Net;
using System.Text.Json;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public sealed class AccountDataExportApiTests
{
    [Fact]
    public async Task Export_is_private_attachment_scoped_by_relationship_and_allowlists_secrets()
    {
        using var scenario=await ReportTestScenario.Create(); using var a=await AccountTestHelpers.Create(scenario,"export-a");
        using var b=await AccountTestHelpers.Create(scenario,"export-b");
        await scenario.Db(async db=>
        {
            var owned=new Order{Number="OWNED-EXPORT",CustomerId=a.Id,Email=a.Email,GiftCardCode="GIFT-SECRET-MARKER"};
            owned.MarkPaid("PAYMENT-SECRET-MARKER",DateTimeOffset.UtcNow);
            db.Orders.AddRange(owned,
                new Order{Number="OTHER-SECRET-MARKER",CustomerId=b.Id,Email=a.Email},
                new Order{Number="GUEST-SAME-EMAIL",Email=a.Email});
            db.Reviews.Add(new Review(db.Products.Select(p=>p.Id).First(),b.Id,5,"OTHER-REVIEW","OTHER-BODY-MARKER"));
            var customer=await db.Customers.SingleAsync(c=>c.Id==a.Id); customer.PasswordHash="PASSWORD-SECRET-MARKER";
            db.Set<LoginSession>().Add(new LoginSession(a.Id,"Customer","SESSION-SECRET-MARKER",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(1)));
            db.WebhookSubscriptions.Add(new WebhookSubscription{Url="https://example.test/hook",Secret="WEBHOOK-SECRET-MARKER",Events=[WebhookEvents.OrderPaid]});
            db.Set<IntegrationApiKey>().Add(new IntegrationApiKey(Guid.NewGuid(),"INTEGRATION-SECRET-MARKER",new byte[32],IntegrationKeyScope.CatalogRead,a.Id,DateTimeOffset.UtcNow,1));
            db.OrderSupportNotes.Add(new OrderSupportNote(owned.Id,a.Id,"INTERNAL-NOTE-SECRET-MARKER",DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });
        var response=await a.Client.PostAsync("/api/me/data-export",null);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
        Assert.Equal("application/json",response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment",response.Content.Headers.ContentDisposition!.DispositionType);
        var text=await response.Content.ReadAsStringAsync();
        Assert.Contains("OWNED-EXPORT",text); Assert.DoesNotContain("OTHER-SECRET-MARKER",text); Assert.DoesNotContain("GUEST-SAME-EMAIL",text);
        Assert.DoesNotContain("OTHER-BODY-MARKER",text); Assert.DoesNotContain("passwordHash",text,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("giftCardCode",text,StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("paymentTransactionId",text,StringComparison.OrdinalIgnoreCase);
        foreach(var marker in new[]{"PASSWORD-SECRET-MARKER","GIFT-SECRET-MARKER","PAYMENT-SECRET-MARKER","SESSION-SECRET-MARKER","WEBHOOK-SECRET-MARKER","INTEGRATION-SECRET-MARKER","INTERNAL-NOTE-SECRET-MARKER"})Assert.DoesNotContain(marker,text);
        using var json=JsonDocument.Parse(text); Assert.Equal(1,json.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Empty_account_exports_and_anonymous_request_is_rejected_without_mutating_observations()
    {
        using var scenario=await ReportTestScenario.Create(); using var owner=await AccountTestHelpers.Create(scenario,"export-empty");
        var response=await owner.Client.PostAsync("/api/me/data-export",null); response.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("orders").EnumerateArray()); Assert.Empty(json.RootElement.GetProperty("wishlists").EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized,(await scenario.App.CreateClient().PostAsync("/api/me/data-export",null)).StatusCode);
    }
}
