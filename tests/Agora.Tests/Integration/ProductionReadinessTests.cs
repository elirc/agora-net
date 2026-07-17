using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Tests.Integration;

public class ProductionReadinessTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly AgoraApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly AddressDto Address = new(
        "Prod Ready", "1 Uptime Ave", null, "Stableton", "ST", "77777", "US");

    [Fact]
    public async Task HealthReady_ReportsHealthy_WithDbProbe()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_LivenessStillAvailable()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_IsRateLimited_PerConfiguredWindow()
    {
        // A dedicated host with a tiny limit; other tests run with a huge one.
        using var limitedFactory = new AgoraApiFactory();
        using var limited = limitedFactory
            .WithWebHostBuilder(b => b.UseSetting("RateLimiting:Checkout:PermitLimit", "2"))
            .CreateClient();

        // Even invalid checkouts count: the limiter sits in front of the endpoint.
        var first = await limited.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest("nope", "a@b.com", Address, null, "tok_visa"));
        var second = await limited.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest("nope", "a@b.com", Address, null, "tok_visa"));
        var third = await limited.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest("nope", "a@b.com", Address, null, "tok_visa"));

        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task RateLimit_DoesNotAffectOtherEndpoints()
    {
        using var limitedFactory = new AgoraApiFactory();
        using var limited = limitedFactory
            .WithWebHostBuilder(b => b.UseSetting("RateLimiting:Checkout:PermitLimit", "1"))
            .CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var response = await limited.GetAsync("/api/products");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task ConcurrentStockWrites_SecondWriterGetsConcurrencyConflict()
    {
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider
            .GetRequiredService<Agora.Infrastructure.Persistence.AgoraDbContext>();
        var db2 = scope2.ServiceProvider
            .GetRequiredService<Agora.Infrastructure.Persistence.AgoraDbContext>();

        // Both contexts load the same stock snapshot.
        var first = await db1.InventoryItems
            .Include(i => i.ProductVariant)
            .FirstAsync(i => i.ProductVariant!.Sku == "TEE-BLK-S");
        var second = await db2.InventoryItems
            .Include(i => i.ProductVariant)
            .FirstAsync(i => i.ProductVariant!.Sku == "TEE-BLK-S");

        first.Restock(1);
        await db1.SaveChangesAsync(); // wins

        second.Restock(1);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentCartWrites_SecondWriterGetsConcurrencyConflict()
    {
        var capVariantId = (await _client.GetFromJsonAsync<InventoryResponse>(
            "/api/inventory/CAP-KHK"))!.ProductVariantId;
        var teeVariantId = (await _client.GetFromJsonAsync<InventoryResponse>(
            "/api/inventory/TEE-BLK-S"))!.ProductVariantId;
        var cartResponse = await _client.PostAsync("/api/carts", null);
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider
            .GetRequiredService<Agora.Infrastructure.Persistence.AgoraDbContext>();
        var db2 = scope2.ServiceProvider
            .GetRequiredService<Agora.Infrastructure.Persistence.AgoraDbContext>();

        var first = await db1.Carts.Include(c => c.Items).FirstAsync(c => c.Token == token);
        var second = await db2.Carts.Include(c => c.Items).FirstAsync(c => c.Token == token);

        // Different variants: the clash is the cart's version token, not the
        // (CartId, VariantId) unique index.
        db1.CartItems.Add(first.AddItem(capVariantId, 1));
        await db1.SaveChangesAsync(); // wins

        db2.CartItems.Add(second.AddItem(teeVariantId, 2));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task StockMutationsThroughApi_StillWork_WithVersioning()
    {
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        var update = await admin.PutAsJsonAsync("/api/inventory/CDL-CDR-S", new SetStockRequest(123));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var stock = await _client.GetFromJsonAsync<InventoryResponse>("/api/inventory/CDL-CDR-S");
        Assert.Equal(123, stock!.QuantityOnHand);
    }

    [Fact]
    public async Task PaginationAudit_ListEndpointsRejectOversizedPages()
    {
        var admin = _factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        foreach (var path in new[]
                 {
                     "/api/products?pageSize=101",
                     "/api/categories?pageSize=101",
                     "/api/discounts?pageSize=101",
                     "/api/gift-cards?pageSize=101",
                     "/api/webhooks?pageSize=101",
                     "/api/returns?pageSize=101",
                     "/api/reviews?pageSize=101",
                     "/api/me/orders?pageSize=101",
                     "/api/me/returns?pageSize=101",
                     "/api/admin/reports/low-stock?pageSize=101",
                 })
        {
            var response = await admin.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task PaginationAudit_PagedListsReturnEnvelope()
    {
        var categories = await _client.GetFromJsonAsync<PagedResult<CategoryResponse>>(
            "/api/categories?page=1&pageSize=2");

        Assert.NotNull(categories);
        Assert.Equal(2, categories.Items.Count);
        Assert.True(categories.TotalCount >= 3);
        Assert.True(categories.TotalPages >= 2);

        var discounts = await _client.GetFromJsonAsync<PagedResult<DiscountResponse>>(
            "/api/discounts?page=1&pageSize=1");
        Assert.Single(discounts!.Items);
        Assert.True(discounts.TotalCount >= 3);
    }
}
