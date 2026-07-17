using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class InventoryApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Admin-only mutations are exercised throughout; authenticate up front.
    public Task InitializeAsync() => _client.AuthenticateAsAdminAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetBySku_ReturnsSeededStock()
    {
        var inventory = await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/TEE-BLK-M");

        Assert.NotNull(inventory);
        Assert.Equal(55, inventory.QuantityOnHand);
        Assert.Equal(0, inventory.QuantityReserved);
        Assert.Equal(55, inventory.QuantityAvailable);
    }

    [Fact]
    public async Task GetBySku_Unknown_Returns404()
    {
        var response = await _client.GetAsync("/api/inventory/NOPE-404");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetStock_UpdatesOnHand()
    {
        var response = await _client.PutAsJsonAsync("/api/inventory/CAP-KHK", new SetStockRequest(75));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<InventoryResponse>();
        Assert.Equal(75, updated!.QuantityOnHand);
        Assert.Equal(75, updated.QuantityAvailable);
    }

    [Fact]
    public async Task SetStock_Negative_Returns400()
    {
        var response = await _client.PutAsJsonAsync("/api/inventory/CAP-KHK", new { quantityOnHand = -5 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetStock_BelowReserved_Returns400()
    {
        await factory.WithDbAsync(async db =>
        {
            var item = await db.InventoryItems
                .Include(i => i.ProductVariant)
                .SingleAsync(i => i.ProductVariant!.Sku == "KET-EMB-1L");
            item.Reserve(10);
            await db.SaveChangesAsync();
        });

        var response = await _client.PutAsJsonAsync("/api/inventory/KET-EMB-1L", new SetStockRequest(5));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // DomainException -> 400
    }
}
