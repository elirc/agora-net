using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class OrderTimelineApiTests
{
    [Fact]
    public async Task Timeline_merges_actual_milestones_with_stable_ties_and_pages_without_private_fields_or_writes()
    {
        using var scenario = await ReportTestScenario.Create();
        using var account = await AccountTestHelpers.Create(scenario, "timeline");
        var start = scenario.Clock.Instant.AddDays(-10);
        var order = PackingSlipApiTests.NewOrder(); order.CustomerId = account.Id; order.CreatedAt = start;
        order.MarkPaid("NEVER-PAYMENT", start.AddDays(1)); order.MarkFulfilled(start.AddDays(3));
        var first = PackingSlipApiTests.Shipment(order, 2); first.CreatedAt = start.AddDays(2);
        var last = PackingSlipApiTests.Shipment(order, 3); last.CreatedAt = start.AddDays(3);
        var returned = new ReturnRequest { Number = "RMA-TIMELINE", OrderId = order.Id, CustomerId = account.Id,
            CreatedAt = start.AddDays(4), Comment = "NEVER-COMMENT" };
        returned.Approve("NEVER-REFUND", start.AddDays(5));
        await scenario.Db(async db =>
        {
            var variant = await db.ProductVariants.FirstAsync();
            order.Items[0].ProductVariantId = variant.Id;
            first.Items[0].ProductVariantId = variant.Id; last.Items[0].ProductVariantId = variant.Id;
            db.Orders.Add(order); db.Fulfillments.AddRange(first, last); db.ReturnRequests.Add(returned);
            await db.SaveChangesAsync();
        });
        var path = $"/api/me/orders/{order.Number}/timeline";
        scenario.Commands.Statements.Clear();
        var response = await account.Client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.DoesNotContain("NEVER-", await response.Content.ReadAsStringAsync());
        var page = (await response.Content.ReadFromJsonAsync<PagedResult<OrderTimelineEntry>>())!;
        string[] expected = [$"order-created:{order.Id}", $"order-paid:{order.Id}", $"fulfillment-created:{first.Id}",
            $"fulfillment-created:{last.Id}", $"order-fulfilled:{order.Id}", $"return-created:{returned.Id}", $"return-processed:{returned.Id}"];
        Assert.Equal(expected, page.Items.Select(e => e.Key));
        Assert.Equal(7, page.TotalCount);
        Assert.Equal("ReturnApproved", page.Items[^1].Type);
        Assert.Equal(page.Items[3].RecordedAt, page.Items[4].RecordedAt);
        // Revocable authentication adds one bounded session/current-role lookup.
        // Keep the timeline's own query budget separate from that access check.
        Assert.Single(scenario.Commands.Statements, sql => sql.Contains("FROM \"LoginSession\""));
        Assert.InRange(scenario.Commands.Statements.Count(sql => !sql.Contains("FROM \"LoginSession\"")), 1, 7);
        Assert.All(scenario.Commands.Statements, sql =>
        {
            Assert.DoesNotContain("UPDATE ", sql); Assert.DoesNotContain("INSERT INTO", sql); Assert.DoesNotContain("DELETE FROM", sql);
            Assert.DoesNotContain("PaymentTransactionId", sql); Assert.DoesNotContain("Comment", sql); Assert.DoesNotContain("Email", sql);
        });
        var paged = new List<string>();
        for (var index = 1; index <= 4; index++)
        {
            var part = (await account.Client.GetFromJsonAsync<PagedResult<OrderTimelineEntry>>(path + $"?pageSize=2&page={index}"))!;
            Assert.Equal(7, part.TotalCount); paged.AddRange(part.Items.Select(e => e.Key));
        }
        Assert.Equal(expected, paged);
        await scenario.Db(async db => Assert.Equal(OrderStatus.Fulfilled, (await db.Orders.SingleAsync(o => o.Id == order.Id)).Status));
    }

    [Fact]
    public async Task Ownership_missing_timestamps_and_offset_limit_are_enforced_without_inventing_history()
    {
        using var scenario = await ReportTestScenario.Create();
        using var owner = await AccountTestHelpers.Create(scenario, "timeline-owner");
        using var other = await AccountTestHelpers.Create(scenario, "timeline-other");
        var order = PackingSlipApiTests.NewOrder(); order.CustomerId = owner.Id;
        order.MarkPaid("pay", scenario.Clock.Instant); order.MarkFulfilled(scenario.Clock.Instant);
        var guest = PackingSlipApiTests.NewOrder(); guest.Email = owner.Email;
        var legacyReturn = new ReturnRequest { Number = "RMA-LEGACY", OrderId = order.Id, CustomerId = owner.Id };
        legacyReturn.Approve("refund", scenario.Clock.Instant);
        await scenario.Db(async db =>
        {
            db.Orders.AddRange(order, guest); db.ReturnRequests.Add(legacyReturn);
            db.Entry(order).Property(o => o.PaidAt).CurrentValue = null;
            db.Entry(order).Property(o => o.FulfilledAt).CurrentValue = null;
            db.Entry(legacyReturn).Property(r => r.ProcessedAt).CurrentValue = null;
            await db.SaveChangesAsync();
        });
        var path = $"/api/me/orders/{order.Number}/timeline";
        var response = (await owner.Client.GetFromJsonAsync<PagedResult<OrderTimelineEntry>>(path))!;
        Assert.Equal(2, response.TotalCount);
        Assert.DoesNotContain(response.Items, e => e.Type is "OrderPaid" or "OrderFulfilled" or "ReturnApproved");
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/me/orders/{guest.Number}/timeline")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync("/api/me/orders/missing/timeline")).StatusCode);
        using var visitor = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(path)).StatusCode);
        foreach (var query in new[] { "page=0", "pageSize=101", "page=102&pageSize=100", "page=2147483647&pageSize=100" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.GetAsync(path + "?" + query)).StatusCode);
        var far = (await owner.Client.GetFromJsonAsync<PagedResult<OrderTimelineEntry>>(path + "?page=101&pageSize=100"))!;
        Assert.Empty(far.Items); Assert.Equal(2, far.TotalCount);
    }
}
