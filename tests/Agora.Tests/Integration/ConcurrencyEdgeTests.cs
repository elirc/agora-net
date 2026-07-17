using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Tests.Integration;

/// <summary>
/// Interleaved-writer races on the money- and stock-bearing rows: the last
/// unit of stock, a gift card balance, and webhook redelivery. In every case
/// the second writer must fail loudly instead of silently losing an update.
/// </summary>
public class ConcurrencyEdgeTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly AddressDto Address = new(
        "Racy Writer", "7 Interleave Ln", null, "Raceford", "RC", "66666", "US");

    [Fact]
    public async Task ReservingTheLastUnit_FromTwoSnapshots_SecondWriterConflicts()
    {
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        (await admin.PutAsJsonAsync("/api/inventory/EAR-AUR-WHT", new SetStockRequest(1)))
            .EnsureSuccessStatusCode();

        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<AgoraDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<AgoraDbContext>();

        // Two checkouts each see 1 available and reserve it in memory.
        var first = await db1.InventoryItems
            .Include(i => i.ProductVariant)
            .FirstAsync(i => i.ProductVariant!.Sku == "EAR-AUR-WHT");
        var second = await db2.InventoryItems
            .Include(i => i.ProductVariant)
            .FirstAsync(i => i.ProductVariant!.Sku == "EAR-AUR-WHT");

        first.Reserve(1);
        second.Reserve(1); // legal against its own stale snapshot

        await db1.SaveChangesAsync(); // wins the race

        // The loser must hit the version token, not double-sell the unit.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());

        var stock = await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/EAR-AUR-WHT");
        Assert.Equal(1, stock!.QuantityReserved);
    }

    [Fact]
    public async Task GiftCardDoubleRedemption_SecondWriterConflicts()
    {
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        var issue = await admin.PostAsJsonAsync("/api/gift-cards",
            new IssueGiftCardRequest(50m, null, null));
        var code = (await issue.Content.ReadFromJsonAsync<GiftCardResponse>())!.Code;

        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<AgoraDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<AgoraDbContext>();

        // Two checkouts load the same 50.00 balance and both tender all of it.
        var first = await db1.GiftCards.FirstAsync(g => g.Code == code);
        var second = await db2.GiftCards.FirstAsync(g => g.Code == code);

        first.Redeem(50m);
        second.Redeem(50m);

        await db1.SaveChangesAsync(); // wins

        // Without a version token this would silently overwrite the drained
        // balance and mint 50.00 of free tender for the second order.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());

        var balance = await _client.GetFromJsonAsync<GiftCardResponse>($"/api/gift-cards/{code}");
        Assert.Equal(0m, balance!.Balance);
    }

    [Fact]
    public async Task GiftCard_DrainedByFirstCheckout_SecondCheckoutRejectsIt()
    {
        // The sequential shape of the same race: once the first checkout wins,
        // a later checkout must see the drained card as unusable up front.
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        var issue = await admin.PostAsJsonAsync("/api/gift-cards",
            new IssueGiftCardRequest(60m, null, null));
        var code = (await issue.Content.ReadFromJsonAsync<GiftCardResponse>())!.Code;

        var first = await Checkout(await CartWith("TEE-BLK-S", 2), code); // total 49.17
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await Checkout(await CartWith("HOOD-GRY-M", 1), code); // needs > 10.83
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var order = await second.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal(10.83m, order!.GiftCardAmount); // only the remainder, never re-minted

        var third = await Checkout(await CartWith("CAP-KHK", 1), code);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, third.StatusCode); // drained card
    }

    [Fact]
    public async Task WebhookRedelivery_OfSucceededDelivery_Returns409()
    {
        // Successful deliveries are terminal: manual retry must not fire the
        // same event at the receiver twice.
        using var localFactory = new AgoraApiFactory();
        var client = localFactory.CreateClient();
        var admin = localFactory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        var subscribe = await admin.PostAsJsonAsync("/api/webhooks",
            new SaveWebhookSubscriptionRequest(
                "https://example.com/once", "idempotency-secret", ["order.paid"], null));
        var subscriptionId =
            (await subscribe.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>())!.Id;

        var checkout = await client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(await CartWith("CHG-65W", 1, client), "hooked@example.com",
                Address, null, "tok_visa"));
        checkout.EnsureSuccessStatusCode();

        var deliveries = await admin.GetFromJsonAsync<PagedResult<WebhookDeliveryResponse>>(
            $"/api/webhooks/{subscriptionId}/deliveries");
        var delivery = Assert.Single(deliveries!.Items);
        Assert.Equal("Succeeded", delivery.Status);

        var retry = await admin.PostAsync($"/api/webhooks/deliveries/{delivery.Id}/retry", null);

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        var after = await admin.GetFromJsonAsync<PagedResult<WebhookDeliveryResponse>>(
            $"/api/webhooks/{subscriptionId}/deliveries");
        Assert.Equal(1, Assert.Single(after!.Items).AttemptCount); // no second attempt recorded
    }

    private async Task<string> CartWith(string sku, int quantity, HttpClient? client = null)
    {
        client ??= _client;
        var cartResponse = await client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var inventory = await client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        (await client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, quantity)))
            .EnsureSuccessStatusCode();
        return token;
    }

    private Task<HttpResponseMessage> Checkout(string token, string? giftCardCode) =>
        _client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "race@example.com", Address, null, "tok_visa",
                null, null, giftCardCode));
}
