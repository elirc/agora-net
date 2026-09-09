using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class ReplenishmentReportApiTests
{
    [Fact]
    public async Task Separate_aggregates_keep_sales_single_and_count_only_currently_approved_cohort_returns()
    {
        using var scenario = await ReportTestScenario.Create();
        var now = scenario.Clock.Instant;
        Guid variantId = default;
        ReturnRequest requested = null!;
        await scenario.Db(async db =>
        {
            var variant = await db.ProductVariants.Include(v => v.Inventory).FirstAsync(v => v.Inventory != null && v.Product!.IsActive);
            variantId = variant.Id;
            variant.Inventory!.SetStock(5); variant.Inventory.Reserve(2);
            var order = Sale(variant.Id, 30, now.AddDays(-15));
            order.MarkFulfilled(now.AddDays(-14));
            db.Orders.Add(order);
            db.ReturnRequests.AddRange(Return(order, 2, ReturnStatus.Approved, now.AddDays(1)), Return(order, 4, ReturnStatus.Approved, now.AddDays(1)), Return(order, 7, ReturnStatus.Rejected, now));
            requested = Return(order, 3, ReturnStatus.Requested, now);
            db.ReturnRequests.Add(requested);
            await db.SaveChangesAsync();
        });
        const string path = "/api/admin/reports/replenishment?windowDays=30&coverDays=10";
        scenario.Commands.Statements.Clear();
        var report = (await scenario.Admin.GetFromJsonAsync<ReplenishmentReportResponse>(path))!;
        Assert.Equal((now, now.AddDays(-30), now, 30, 10), (report.AsOf, report.From, report.To, report.WindowDays, report.CoverDays));
        var row = Assert.Single(report.Variants.Items);
        Assert.Equal(variantId, row.VariantId);
        Assert.Equal((24L, 0.8m, 3L, 5L), (row.NetUnits, row.DailyAverage, row.AvailableUnits, row.SuggestedUnits));
        Assert.InRange(scenario.Commands.Statements.Count, 1, 4);
        Assert.All(scenario.Commands.Statements, sql => { Assert.DoesNotContain("INSERT INTO", sql); Assert.DoesNotContain("UPDATE ", sql); Assert.DoesNotContain("DELETE FROM", sql); });
        await scenario.Db(async db =>
        {
            var stock = await db.InventoryItems.SingleAsync(i => i.ProductVariantId == variantId);
            Assert.Equal((5, 2), (stock.QuantityOnHand, stock.QuantityReserved));
            var actual = await db.ReturnRequests.SingleAsync(r => r.Id == requested.Id);
            actual.Approve("later-refund", now.AddDays(2));
            await db.SaveChangesAsync();
        });
        report = (await scenario.Admin.GetFromJsonAsync<ReplenishmentReportResponse>(path))!;
        row = Assert.Single(report.Variants.Items);
        Assert.Equal((21L, 0.7m, 4L), (row.NetUnits, row.DailyAverage, row.SuggestedUnits));
    }

    [Fact]
    public async Task Cohort_boundaries_status_current_catalog_and_ceiling_are_applied_before_stable_paging()
    {
        using var scenario = await ReportTestScenario.Create();
        var now = scenario.Clock.Instant;
        Guid[] variants = [];
        await scenario.Db(async db =>
        {
            var current = await db.ProductVariants.Include(v => v.Inventory).Include(v => v.Product)
                .Where(v => v.Inventory != null && v.Product!.IsActive).OrderBy(v => v.Id).Take(4).ToListAsync();
            variants = current.Select(v => v.Id).ToArray();
            foreach (var v in current) v.Inventory!.SetStock(0);
            db.Orders.AddRange(Sale(variants[0], 1, now.AddDays(-30)), Sale(variants[1], 1, now.AddTicks(-1)));
            db.Orders.AddRange(Sale(variants[0], 500, now.AddDays(-30).AddTicks(-1)), Sale(variants[0], 500, now));
            var refunded = Sale(variants[0], 500, now.AddDays(-1)); refunded.Refund(now);
            var cancelled = Sale(variants[0], 500, now.AddDays(-1)); cancelled.Cancel(now);
            var pending = PackingSlipApiTests.NewOrder(); pending.Items[0].ProductVariantId = variants[0]; pending.Items[0].Quantity = 500;
            db.Orders.AddRange(refunded, cancelled, pending);
            // A historical line can outlive its catalog variant; no matching current variant means no suggestion.
            db.Orders.Add(Sale(Guid.NewGuid(), 500, now.AddDays(-1)));
            // Use an independent product to avoid deactivating another tested variant through a shared parent.
            var inactiveProduct = new Product { Name = "Inactive demand", Slug = "inactive-" + Guid.NewGuid().ToString("N"), CategoryId = current[0].Product!.CategoryId, IsActive = false };
            var inactive = new ProductVariant { ProductId = inactiveProduct.Id, Sku = "INACTIVE-" + Guid.NewGuid().ToString("N"), Name = "Inactive" };
            inactive.Inventory = new InventoryItem(inactive.Id, 0); inactiveProduct.Variants.Add(inactive);
            db.Products.Add(inactiveProduct); db.Orders.Add(Sale(inactive.Id, 500, now.AddDays(-1)));
            await db.SaveChangesAsync();
        });
        const string path = "/api/admin/reports/replenishment?windowDays=30&coverDays=10&pageSize=1";
        scenario.Commands.Statements.Clear();
        var first = (await scenario.Admin.GetFromJsonAsync<ReplenishmentReportResponse>(path))!;
        var commands = scenario.Commands.Statements.Count;
        scenario.Commands.Statements.Clear();
        var second = (await scenario.Admin.GetFromJsonAsync<ReplenishmentReportResponse>(path + "&page=2"))!;
        Assert.Equal(commands, scenario.Commands.Statements.Count);
        Assert.Equal(2, first.Variants.TotalCount);
        Assert.Equal(2, second.Variants.TotalCount);
        var rows = first.Variants.Items.Concat(second.Variants.Items).ToArray();
        Assert.Equal(variants.Take(2), rows.Select(r => r.VariantId));
        Assert.All(rows, row => { Assert.Equal(1, row.NetUnits); Assert.Equal(1, row.SuggestedUnits); });
        var page3 = (await scenario.Admin.GetFromJsonAsync<ReplenishmentReportResponse>(path + "&page=3"))!;
        Assert.Empty(page3.Variants.Items);
        Assert.Equal(2, page3.Variants.TotalCount);
    }

    [Fact]
    public async Task Bounds_authorization_empty_cohorts_and_negative_net_have_explicit_results()
    {
        using var scenario = await ReportTestScenario.Create();
        const string path = "/api/admin/reports/replenishment";
        var empty = (await scenario.Admin.GetFromJsonAsync<ReplenishmentReportResponse>(path))!;
        Assert.Empty(empty.Variants.Items);
        Assert.Equal((30, 14), (empty.WindowDays, empty.CoverDays));
        foreach (var query in new[] { "windowDays=6", "windowDays=91", "coverDays=0", "coverDays=61", "page=0", "pageSize=101", "page=2147483647&pageSize=100" })
            Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.GetAsync(path + "?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.GetAsync(path + "?windowDays=7&coverDays=1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.GetAsync(path + "?windowDays=90&coverDays=60")).StatusCode);
        using var visitor = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(path)).StatusCode);
        visitor.UseBearer(await TestAuth.RegisterAsync(visitor, $"demand-{Guid.NewGuid():N}@example.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.GetAsync(path)).StatusCode);
        await scenario.Db(async db =>
        {
            var variant = await db.ProductVariants.FirstAsync(v => v.Product!.IsActive);
            var order = Sale(variant.Id, 1, scenario.Clock.Instant.AddDays(-1));
            db.Orders.Add(order); db.ReturnRequests.Add(Return(order, 2, ReturnStatus.Approved, scenario.Clock.Instant));
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.GetAsync(path)).StatusCode);
    }

    private static Order Sale(Guid variant, int quantity, DateTimeOffset paid)
    {
        var order = PackingSlipApiTests.NewOrder();
        order.Items[0].ProductVariantId = variant; order.Items[0].Quantity = quantity;
        order.MarkPaid("payment", paid);
        return order;
    }
    private static ReturnRequest Return(Order order, int quantity, ReturnStatus status, DateTimeOffset processed)
    {
        var result = new ReturnRequest { Number = "RMA-" + Guid.NewGuid().ToString("N"), OrderId = order.Id };
        result.Items.Add(new ReturnRequestItem { ReturnRequestId = result.Id, OrderItemId = order.Items[0].Id,
            ProductVariantId = order.Items[0].ProductVariantId, Sku = order.Items[0].Sku, Quantity = quantity });
        if (status == ReturnStatus.Approved) result.Approve("refund", processed);
        if (status == ReturnStatus.Rejected) result.Reject("rejected", processed);
        return result;
    }
}
