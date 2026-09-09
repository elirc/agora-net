using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Api.Rendering;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class PackingSlipApiTests
{
    [Fact]
    public async Task Slip_uses_snapshots_encodes_text_counts_shipments_and_does_not_mutate()
    {
        using var factory = new AgoraApiFactory();
        using var admin = factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        var order = NewOrder();
        order.MarkPaid("NEVER-PAYMENT", DateTimeOffset.UtcNow);
        order.MarkPartiallyFulfilled();
        await factory.WithDbAsync(async db =>
        {
            var variant = await db.ProductVariants.Include(v => v.Product).FirstAsync();
            order.Items[0].ProductVariantId = variant.Id;
            variant.Product!.Name = "CURRENT-CATALOG-NAME";
            db.Orders.Add(order);
            db.Fulfillments.Add(Shipment(order, 2));
            await db.SaveChangesAsync();
        });
        var response = await admin.GetAsync($"/api/admin/orders/{order.Number}/packing-slip");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.CacheControl.Private);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("&lt;script&gt;old item&lt;/script&gt;", html);
        Assert.Contains("Old &amp; Recipient", html);
        Assert.Contains("Original address", html);
        Assert.Contains("<td class=\"quantity\">5</td><td class=\"quantity\">2</td><td class=\"quantity\">3</td>", html);
        foreach (var excluded in new[] { "<script>", "NEVER-", "CURRENT-CATALOG-NAME", "12345.67", "http://", "https://", "<img", "<iframe" })
            Assert.DoesNotContain(excluded, html);
        await factory.WithDbAsync(async db =>
        {
            var unchanged = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PartiallyFulfilled, unchanged.Status);
            Assert.Equal(5, Assert.Single(unchanged.Items).Quantity);
            Assert.Equal(2, await db.FulfillmentItems.Where(i => i.OrderItemId == order.Items[0].Id).SumAsync(i => i.Quantity));
        });
    }

    [Fact]
    public async Task Access_status_line_cap_and_inconsistent_quantities_are_enforced()
    {
        using var factory = new AgoraApiFactory();
        using var admin = factory.CreateClient();
        using var publicClient = factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        var pending = NewOrder();
        var cancelled = NewOrder(); cancelled.Cancel(DateTimeOffset.UtcNow);
        var refunded = NewOrder(); refunded.MarkPaid("pay", DateTimeOffset.UtcNow); refunded.Refund(DateTimeOffset.UtcNow);
        var large = NewOrder(); large.MarkPaid("pay", DateTimeOffset.UtcNow);
        for (var i = 1; i <= 500; i++) large.Items.Add(new OrderItem { OrderId = large.Id, Sku = $"L-{i:D4}", Quantity = 1 });
        var invalid = NewOrder(); invalid.MarkPaid("pay", DateTimeOffset.UtcNow);
        var paid = NewOrder(); paid.MarkPaid("pay", DateTimeOffset.UtcNow);
        var full = NewOrder(); full.MarkPaid("pay", DateTimeOffset.UtcNow); full.MarkFulfilled(DateTimeOffset.UtcNow);
        await factory.WithDbAsync(async db =>
        {
            var variant = await db.ProductVariants.FirstAsync();
            foreach (var order in new[] { pending, cancelled, refunded, large, invalid, paid, full })
                foreach (var line in order.Items) line.ProductVariantId = variant.Id;
            db.Orders.AddRange(pending, cancelled, refunded, large, invalid, paid, full);
            db.Fulfillments.AddRange(Shipment(invalid, 6), Shipment(full, 5));
            await db.SaveChangesAsync();
        });
        string Path(Order o) => $"/api/admin/orders/{o.Number}/packing-slip";
        Assert.Equal(HttpStatusCode.Unauthorized, (await publicClient.GetAsync(Path(paid))).StatusCode);
        publicClient.UseBearer(await TestAuth.RegisterAsync(publicClient, $"packing-{Guid.NewGuid():N}@example.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await publicClient.GetAsync(Path(paid))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/admin/orders/missing/packing-slip")).StatusCode);
        foreach (var order in new[] { pending, cancelled, refunded, invalid })
            Assert.Equal(HttpStatusCode.Conflict, (await admin.GetAsync(Path(order))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync(Path(large))).StatusCode);
        Assert.Contains("<td class=\"quantity\">5</td><td class=\"quantity\">0</td><td class=\"quantity\">5</td>", await admin.GetStringAsync(Path(paid)));
        Assert.Contains("<td class=\"quantity\">5</td><td class=\"quantity\">5</td><td class=\"quantity\">0</td>", await admin.GetStringAsync(Path(full)));
    }

    [Fact]
    public void Renderer_encodes_every_string_field_and_keeps_print_assets_local()
    {
        const string hostile = "<script>&\"'";
        var model = new PackingSlipModel(hostile, DateTimeOffset.UnixEpoch,
            new PackingSlipAddress(hostile, hostile, hostile, hostile, hostile, hostile, hostile),
            [new PackingSlipLine(hostile, hostile, hostile, 5, 2, 3)]);
        var html = PackingSlipRenderer.Render(model);
        Assert.DoesNotContain("<script>", html);
        Assert.Equal(11, html.Split("&lt;script&gt;").Length - 1);
        Assert.Contains("table-header-group", html);
        Assert.Contains("overflow-wrap: anywhere", html);
        Assert.Contains("@media print", html);
    }

    internal static Order NewOrder()
    {
        var order = new Order
        {
            Number = "PACK-" + Guid.NewGuid().ToString("N"), Email = "NEVER-EMAIL@example.test",
            GiftCardCode = "NEVER-GIFT", Total = 12345.67m,
            ShippingAddress = new Address { FullName = "Old & Recipient", Line1 = "Original address", City = "Town", Country = "GB" },
            ShippingMethodCode = "SNAPSHOT", ShippingMethodName = "Original shipping",
        };
        order.Items.Add(new OrderItem { OrderId = order.Id, Sku = "OLD-SKU", ProductName = "<script>old item</script>",
            VariantName = "Old variant", Quantity = 5, UnitPrice = 12345.67m });
        return order;
    }

    internal static Fulfillment Shipment(Order order, int quantity, OrderItem? item = null)
    {
        item ??= order.Items[0];
        var shipment = new Fulfillment { Number = "SHIP-" + Guid.NewGuid().ToString("N"), OrderId = order.Id, Carrier = "Manual" };
        shipment.Items.Add(new FulfillmentItem { FulfillmentId = shipment.Id, OrderItemId = item.Id,
            ProductVariantId = item.ProductVariantId, Sku = item.Sku, Quantity = quantity });
        return shipment;
    }
}
