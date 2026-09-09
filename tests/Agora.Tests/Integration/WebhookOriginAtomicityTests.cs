using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Tests.Integration;

public sealed class WebhookOriginAtomicityTests
{
    [Fact]
    public async Task Checkout_business_write_and_staged_events_roll_back_as_one_unit()
    {
        using var factory = new WebhookApiFactory();
        using var client = factory.CreateClient();
        Guid variantId;
        int stockBefore;

        using (var setupScope = factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<AgoraDbContext>();
            var variant = await db.ProductVariants.Include(x => x.Inventory).SingleAsync(x => x.Sku == "TEE-BLK-S");
            variantId = variant.Id;
            stockBefore = variant.Inventory!.QuantityOnHand;
            db.WebhookSubscriptions.Add(new WebhookSubscription
            {
                Url = "https://example.test/atomic",
                Secret = "sixteen-character-secret",
                Events = [WebhookEvents.OrderCreated, WebhookEvents.OrderPaid]
            });
            await db.SaveChangesAsync();
        }

        var cartResponse = await client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var cart = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!;
        (await client.PostAsJsonAsync($"/api/carts/{cart.Token}/items", new AddCartItemRequest(variantId, 1)))
            .EnsureSuccessStatusCode();

        using (var triggerScope = factory.Services.CreateScope())
        {
            var db = triggerScope.ServiceProvider.GetRequiredService<AgoraDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_origin_event BEFORE INSERT ON OutboxEvents BEGIN SELECT RAISE(ABORT, 'forced outbox failure'); END;");
        }

        var response = await client.PostAsJsonAsync("/api/checkout", new CheckoutRequest(
            cart.Token,
            "atomic@example.test",
            new AddressDto("Atomic Learner", "1 Transaction Way", null, "Town", "VA", "22201", "US"),
            null,
            "tok_visa"));
        Assert.False(response.IsSuccessStatusCode);

        using var assertScope = factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<AgoraDbContext>();
        await assertDb.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_origin_event;");
        var order = await assertDb.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Empty(await assertDb.Set<GuestOrderCredential>().ToListAsync());
        Assert.Empty(await assertDb.Set<OutboxEvent>().ToListAsync());
        Assert.Empty(await assertDb.WebhookDeliveries.ToListAsync());
        var inventory = await assertDb.InventoryItems.SingleAsync(x => x.ProductVariantId == variantId);
        Assert.Equal(stockBefore, inventory.QuantityOnHand);
        Assert.Equal(1, inventory.QuantityReserved);
    }
}
