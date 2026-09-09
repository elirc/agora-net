using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;

namespace Agora.Tests.Integration;

/// <summary>
/// Reporting tests run against a private factory so the numbers are not
/// polluted by orders from other test classes.
/// </summary>
public class AdminReportsApiTests : IClassFixture<AgoraApiFactory>
{
    private static readonly AddressDto Address = new(
        "Report Reader", "12 Ledger Way", null, "Metricsburg", "MB", "66666", "US");

    [Fact]
    public async Task Reports_AreAdminOnly()
    {
        using var factory = new AgoraApiFactory();
        var client = factory.CreateClient();
        var customer = factory.CreateClient();
        customer.UseBearer(await TestAuth.RegisterAsync(customer, "report-nobody@example.com"));

        foreach (var path in new[]
                 {
                     "/api/admin/reports/sales",
                     "/api/admin/reports/top-products",
                     "/api/admin/reports/low-stock",
                     "/api/admin/reports/discount-usage",
                 })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(path)).StatusCode);
        }
    }

    [Fact]
    public async Task SalesReport_BucketsAndTotalsMatchPlacedOrders()
    {
        using var factory = new AgoraApiFactory();
        var client = factory.CreateClient();
        var admin = factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        // 2 x 19.99 -> total 49.17; 1 x 24.00 -> total 31.91 (tax 1.92 + ship 5.99)
        await PlaceOrder(client, "TEE-BLK-S", 2);
        await PlaceOrder(client, "CAP-KHK", 1);

        var report = await admin.GetFromJsonAsync<SalesReportResponse>(
            "/api/admin/reports/sales?interval=day");

        Assert.Equal(2, report!.TotalOrders);
        Assert.Equal(81.08m, report.TotalRevenue); // 49.17 + 31.91
        var bucket = Assert.Single(report.Buckets);
        Assert.Equal(2, bucket.OrderCount);
        Assert.Equal(3, bucket.ItemsSold);
        Assert.Equal(81.08m, bucket.GrossRevenue);
    }

    [Fact]
    public async Task SalesReport_InvalidInterval_Returns400()
    {
        using var factory = new AgoraApiFactory();
        var admin = factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        var response = await admin.GetAsync("/api/admin/reports/sales?interval=hourly");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TopProducts_OrdersByRevenue()
    {
        using var factory = new AgoraApiFactory();
        var client = factory.CreateClient();
        var admin = factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        await PlaceOrder(client, "TEE-BLK-S", 3);  // 59.97 revenue
        await PlaceOrder(client, "EAR-AUR-BLK", 1); // 129.99 revenue

        var top = await admin.GetFromJsonAsync<List<TopProductResponse>>(
            "/api/admin/reports/top-products?limit=10");

        Assert.Equal(2, top!.Count);
        Assert.Equal("EAR-AUR-BLK", top[0].Sku);
        Assert.Equal(129.99m, top[0].Revenue);
        Assert.Equal(1, top[0].UnitsSold);
        Assert.Equal("TEE-BLK-S", top[1].Sku);
        Assert.Equal(59.97m, top[1].Revenue);
        Assert.Equal(3, top[1].UnitsSold);
    }

    [Fact]
    public async Task LowStock_ListsVariantsAtOrBelowThreshold()
    {
        using var factory = new AgoraApiFactory();
        var admin = factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        var report = (await admin.GetFromJsonAsync<PagedResult<LowStockResponse>>(
            "/api/admin/reports/low-stock?threshold=9"))!.Items;

        // Seeded: CDL-CDR-L has 0, KB-NIM-RED has 9.
        Assert.Contains(report!, r => r.Sku == "CDL-CDR-L" && r.QuantityAvailable == 0);
        Assert.Contains(report!, r => r.Sku == "KB-NIM-RED" && r.QuantityAvailable == 9);
        Assert.DoesNotContain(report!, r => r.Sku == "TEE-BLK-M"); // 55 in stock
        // Scarcest first.
        Assert.Equal("CDL-CDR-L", report![0].Sku);
    }

    [Fact]
    public async Task LowStock_ReflectsReservationsAndSales()
    {
        using var factory = new AgoraApiFactory();
        var client = factory.CreateClient();
        var admin = factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        // HOOD-GRY-L starts at 18; sell 15, leaving 3 available.
        await PlaceOrder(client, "HOOD-GRY-L", 15);

        var report = (await admin.GetFromJsonAsync<PagedResult<LowStockResponse>>(
            "/api/admin/reports/low-stock?threshold=5"))!.Items;

        Assert.Contains(report!, r => r.Sku == "HOOD-GRY-L" && r.QuantityAvailable == 3);
    }

    [Fact]
    public async Task DiscountUsage_TracksRedemptions()
    {
        using var factory = new AgoraApiFactory();
        var client = factory.CreateClient();
        var admin = factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();

        await PlaceOrder(client, "HOOD-GRY-M", 1, discount: "WELCOME10"); // 5.45 off
        await PlaceOrder(client, "TEE-BLK-S", 1); // no discount

        var report = await admin.GetFromJsonAsync<List<DiscountUsageResponse>>(
            "/api/admin/reports/discount-usage");

        Assert.NotNull(report);
        var welcome = report.Single(r => r.Code == "WELCOME10");
        Assert.Equal(1, welcome.TimesUsed);
        Assert.Equal(1, welcome.OrderCount);
        Assert.Equal(5.45m, welcome.TotalDiscounted);
        Assert.Equal(58.96m, welcome.TotalRevenue);

        var save5 = report.Single(r => r.Code == "SAVE5");
        Assert.Equal(0, save5.TimesUsed);
        Assert.Equal(0, save5.OrderCount);
    }

    private static async Task<OrderResponse> PlaceOrder(
        HttpClient client, string sku, int quantity, string? discount = null)
    {
        var cartResponse = await client.PostAsync("/api/carts", null);
        cartResponse.EnsureSuccessStatusCode();
        var token = (await cartResponse.Content.ReadFromJsonAsync<CartResponse>())!.Token;

        var inventory = await client.GetFromJsonAsync<InventoryResponse>($"/api/inventory/{sku}");
        (await client.PostAsJsonAsync($"/api/carts/{token}/items",
            new AddCartItemRequest(inventory!.ProductVariantId, quantity))).EnsureSuccessStatusCode();

        var checkout = await client.PostAsJsonAsync("/api/checkout",
            new CheckoutRequest(token, "metrics@example.com", Address, discount, "tok_visa"));
        checkout.EnsureSuccessStatusCode();
        return (await checkout.Content.ReadFromJsonAsync<OrderResponse>())!;
    }
}
