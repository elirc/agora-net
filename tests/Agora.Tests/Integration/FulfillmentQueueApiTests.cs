using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class FulfillmentQueueApiTests
{
    [Fact]
    public async Task Query_count_does_not_grow_with_orders_and_commands_remain_reads()
    {
        using var scenario = await ReportTestScenario.Create();
        await scenario.Db(async db =>
        {
            var variantId = (await db.ProductVariants.FirstAsync()).Id;
            for (var index = 0; index < 3; index++)
            {
                var order = PackingSlipApiTests.NewOrder();
                order.Items[0].ProductVariantId = variantId;
                order.MarkPaid("pay", scenario.Clock.Instant.AddMinutes(index - 10));
                db.Orders.Add(order);
                db.Fulfillments.Add(PackingSlipApiTests.Shipment(order, 1));
            }
            await db.SaveChangesAsync();
        });
        scenario.Commands.Statements.Clear();
        var one = await scenario.Admin.GetAsync("/api/admin/fulfillment-queue?pageSize=1");
        one.EnsureSuccessStatusCode();
        var count = scenario.Commands.Statements.Count;
        Assert.InRange(count, 1, 5);
        scenario.Commands.Statements.Clear();
        var three = await scenario.Admin.GetAsync("/api/admin/fulfillment-queue?pageSize=3");
        three.EnsureSuccessStatusCode();
        Assert.Equal(count, scenario.Commands.Statements.Count);
        Assert.All(scenario.Commands.Statements, sql =>
        {
            Assert.DoesNotContain("INSERT INTO", sql);
            Assert.DoesNotContain("UPDATE ", sql);
            Assert.DoesNotContain("DELETE FROM", sql);
            Assert.DoesNotContain("InventoryItems", sql);
        });
    }

    [Fact]
    public async Task Queue_filters_before_paging_and_counts_each_shipment_once_without_stock_changes()
    {
        using var factory = new AgoraApiFactory();
        using var admin = factory.CreateClient();
        await admin.AuthenticateAsAdminAsync();
        var when = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        var a = PackingSlipApiTests.NewOrder(); a.MarkPaid("pay", when); a.MarkPartiallyFulfilled();
        var b = PackingSlipApiTests.NewOrder(); b.MarkPaid("pay", when);
        var covered = PackingSlipApiTests.NewOrder(); covered.MarkPaid("pay", when.AddDays(-1));
        var pending = PackingSlipApiTests.NewOrder();
        var cancelled = PackingSlipApiTests.NewOrder(); cancelled.MarkPaid("pay", when); cancelled.Cancel(when);
        var refunded = PackingSlipApiTests.NewOrder(); refunded.MarkPaid("pay", when); refunded.Refund(when);
        var full = PackingSlipApiTests.NewOrder(); full.MarkPaid("pay", when); full.MarkFulfilled(when);
        a.Items.Add(new OrderItem { OrderId = a.Id, Sku = "COVERED", Quantity = 1 });
        Guid inventoryId = default;
        await factory.WithDbAsync(async db =>
        {
            var variant = await db.ProductVariants.Include(v => v.Inventory).FirstAsync(v => v.Inventory != null);
            inventoryId = variant.Id;
            // Exhaust the current shelf stock. It must not remove already-paid packing work.
            variant.Inventory!.SetStock(0);
            foreach (var order in new[] { a, b, covered, pending, cancelled, refunded, full })
                foreach (var line in order.Items) line.ProductVariantId = variant.Id;
            db.Orders.AddRange(a, b, covered, pending, cancelled, refunded, full);
            db.Fulfillments.AddRange(PackingSlipApiTests.Shipment(a, 2), PackingSlipApiTests.Shipment(a, 1),
                PackingSlipApiTests.Shipment(a, 1, a.Items[1]), PackingSlipApiTests.Shipment(covered, 5), PackingSlipApiTests.Shipment(full, 5));
            await db.SaveChangesAsync();
        });
        var first = (await admin.GetFromJsonAsync<PagedResult<FulfillmentQueueOrderResponse>>("/api/admin/fulfillment-queue?pageSize=1"))!;
        var second = (await admin.GetFromJsonAsync<PagedResult<FulfillmentQueueOrderResponse>>("/api/admin/fulfillment-queue?pageSize=1&page=2"))!;
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, second.TotalCount);
        var expected = new[] { a, b }.OrderBy(o => o.Id.ToString(), StringComparer.Ordinal).Select(o => o.Number).ToArray();
        Assert.Equal(expected, new[] { Assert.Single(first.Items).Number, Assert.Single(second.Items).Number });
        var rows = first.Items.Concat(second.Items).ToArray();
        var partial = Assert.Single(rows, o => o.Number == a.Number);
        var line = Assert.Single(partial.Lines);
        Assert.Equal((5, 3L, 2L), (line.OrderedQuantity, line.FulfilledQuantity, line.RemainingQuantity));
        Assert.Equal("Original shipping", partial.ShippingMethodName);
        Assert.Equal("<script>old item</script>", line.ProductName);
        var from = Uri.EscapeDataString(when.ToString("O"));
        var to = Uri.EscapeDataString(when.AddDays(1).ToString("O"));
        var included = await admin.GetFromJsonAsync<PagedResult<FulfillmentQueueOrderResponse>>($"/api/admin/fulfillment-queue?paidFrom={from}&paidTo={to}");
        Assert.Equal(2, included!.TotalCount);
        var before = Uri.EscapeDataString(when.AddDays(-1).ToString("O"));
        var excluded = await admin.GetFromJsonAsync<PagedResult<FulfillmentQueueOrderResponse>>($"/api/admin/fulfillment-queue?paidFrom={before}&paidTo={from}");
        Assert.Empty(excluded!.Items);
        await factory.WithDbAsync(async db =>
        {
            var inventory = await db.InventoryItems.SingleAsync(i => i.ProductVariantId == inventoryId);
            Assert.Equal(0, inventory.QuantityOnHand);
            Assert.Equal(0, inventory.QuantityReserved);
            Assert.Equal(OrderStatus.PartiallyFulfilled, (await db.Orders.SingleAsync(o => o.Id == a.Id)).Status);
        });
    }

    [Fact]
    public async Task Invalid_filters_access_and_overfulfillment_have_explicit_responses()
    {
        using var factory = new AgoraApiFactory();
        using var admin = factory.CreateClient();
        using var customer = factory.CreateClient();
        const string path = "/api/admin/fulfillment-queue";
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync(path)).StatusCode);
        customer.UseBearer(await TestAuth.RegisterAsync(customer, $"queue-{Guid.NewGuid():N}@example.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(path)).StatusCode);
        await admin.AuthenticateAsAdminAsync();
        foreach (var query in new[] { "page=0", "pageSize=101", "page=2147483647&pageSize=100",
                     "paidFrom=2026-01-01T00:00:00Z", "paidTo=2026-01-01T00:00:00Z",
                     "paidFrom=2026-01-01T00:00:00Z&paidTo=2026-01-01T00:00:00Z",
                     "paidFrom=2026-01-02T00:00:00Z&paidTo=2026-01-01T00:00:00Z",
                     "paidFrom=2026-01-01T00:00:00Z&paidTo=2026-04-02T00:00:00Z" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(path + "?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(path + "?paidFrom=2026-01-01T00:00:00Z&paidTo=2026-04-01T00:00:00Z")).StatusCode);
        var invalid = PackingSlipApiTests.NewOrder(); invalid.MarkPaid("pay", DateTimeOffset.UtcNow);
        await factory.WithDbAsync(async db =>
        {
            invalid.Items[0].ProductVariantId = (await db.ProductVariants.FirstAsync()).Id;
            db.Orders.Add(invalid); db.Fulfillments.Add(PackingSlipApiTests.Shipment(invalid, 6));
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Conflict, (await admin.GetAsync(path)).StatusCode);
    }
}
