using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class ReorderPoliciesApiTests
{
    [Fact]
    public async Task Default_reads_do_not_insert_and_required_nullable_revision_distinguishes_create_from_replace()
    {
        using var scenario = await ReportTestScenario.Create();
        Guid id = default;
        await scenario.Db(async db => id = (await db.ProductVariants.FirstAsync()).Id);
        var path = $"/api/admin/inventory/{id}/reorder-policy";
        var initial = (await scenario.Admin.GetFromJsonAsync<ReorderPolicyResponse>(path))!;
        Assert.Equal(new ReorderPolicyResponse(id, false, 5, 5, null, null), initial);
        await scenario.Db(async db => Assert.Empty(await db.InventoryReorderPolicies.ToListAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PutAsJsonAsync(path, new { threshold = 5, targetLevel = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PutAsJsonAsync(path, new { threshold = 5, targetLevel = 5, expectedVersion = 0 })).StatusCode);
        var created = await scenario.Admin.PutAsJsonAsync(path, new ReplaceReorderPolicyRequest(5, 5, null));
        created.EnsureSuccessStatusCode();
        var policy = (await created.Content.ReadFromJsonAsync<ReorderPolicyResponse>())!;
        Assert.True(policy.HasOverride);
        Assert.Equal(0L, policy.Version);
        Assert.Equal(scenario.Clock.Instant, policy.UpdatedAt);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PutAsJsonAsync(path, new ReplaceReorderPolicyRequest(5, 5, null))).StatusCode);
        var replaced = await scenario.Admin.PutAsJsonAsync(path, new ReplaceReorderPolicyRequest(0, 0, 0));
        replaced.EnsureSuccessStatusCode();
        policy = (await replaced.Content.ReadFromJsonAsync<ReorderPolicyResponse>())!;
        Assert.Equal((0, 0, 1L), (policy.Threshold, policy.TargetLevel, policy.Version!.Value));
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PutAsJsonAsync(path, new ReplaceReorderPolicyRequest(8, 20, 0))).StatusCode);
        foreach (var invalid in new[] { new ReplaceReorderPolicyRequest(-1, 5, 1), new(6, 5, 1), new(0, 1_000_001, 1), new(0, 0, -1), new(null, 0, 1) })
            Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PutAsJsonAsync(path, invalid)).StatusCode);
        var maximum = await scenario.Admin.PutAsJsonAsync(path, new ReplaceReorderPolicyRequest(1_000_000, 1_000_000, 1));
        maximum.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.GetAsync($"/api/admin/inventory/{Guid.NewGuid()}/reorder-policy")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.PutAsJsonAsync($"/api/admin/inventory/{Guid.NewGuid()}/reorder-policy", new ReplaceReorderPolicyRequest(0, 0, null))).StatusCode);
        using var visitor = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(path)).StatusCode);
        visitor.UseBearer(await TestAuth.RegisterAsync(visitor, $"reorder-{Guid.NewGuid():N}@example.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.PutAsJsonAsync(path, new ReplaceReorderPolicyRequest(0, 0, null))).StatusCode);
    }

    [Fact]
    public async Task Report_uses_available_threshold_equality_computed_defaults_and_stable_pages_without_writes()
    {
        using var scenario = await ReportTestScenario.Create();
        Guid[] ids = [];
        await scenario.Db(async db =>
        {
            var stock = await db.InventoryItems.OrderBy(i => i.ProductVariantId).ToListAsync();
            foreach (var row in stock) row.SetStock(100);
            ids = stock.Take(4).Select(i => i.ProductVariantId).ToArray();
            stock[0].SetStock(12); stock[0].Reserve(4);
            stock[1].SetStock(5); stock[2].SetStock(9); stock[3].SetStock(0);
            db.InventoryReorderPolicies.AddRange(new(ids[0], 8, 20, scenario.Clock.Instant), new(ids[3], 0, 0, scenario.Clock.Instant));
            await db.SaveChangesAsync();
        });
        const string path = "/api/admin/inventory/reorder-report";
        scenario.Commands.Statements.Clear();
        var first = (await scenario.Admin.GetFromJsonAsync<PagedResult<ReorderReportRow>>(path + "?pageSize=1"))!;
        var commandCount = scenario.Commands.Statements.Count;
        Assert.InRange(commandCount, 1, 3);
        Assert.Equal(3, first.TotalCount);
        var row = Assert.Single(first.Items);
        Assert.Equal(ids[0], row.VariantId);
        Assert.Equal((12, 4, 8L, 12L), (row.OnHand, row.Reserved, row.Available, row.SuggestedQuantity));
        scenario.Commands.Statements.Clear();
        var all = (await scenario.Admin.GetFromJsonAsync<PagedResult<ReorderReportRow>>(path + "?pageSize=100"))!;
        Assert.Equal(commandCount, scenario.Commands.Statements.Count);
        Assert.Equal(new[] { ids[0], ids[1], ids[3] }, all.Items.Select(i => i.VariantId));
        var defaultRow = all.Items.Single(i => i.VariantId == ids[1]);
        Assert.False(defaultRow.HasOverride);
        Assert.Equal((5, 5, 0L), (defaultRow.Threshold, defaultRow.TargetLevel, defaultRow.SuggestedQuantity));
        Assert.True(all.Items.Single(i => i.VariantId == ids[3]).HasOverride);
        Assert.All(scenario.Commands.Statements, sql => { Assert.DoesNotContain("UPDATE ", sql); Assert.DoesNotContain("INSERT INTO", sql); Assert.DoesNotContain("DELETE FROM", sql); });
        await scenario.Db(async db =>
        {
            Assert.Equal(2, await db.InventoryReorderPolicies.CountAsync());
            Assert.Equal(12, (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == ids[0])).QuantityOnHand);
        });
        foreach (var query in new[] { "page=0", "pageSize=101", "page=2147483647&pageSize=100" })
            Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.GetAsync(path + "?" + query)).StatusCode);
        using var visitor = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(path)).StatusCode);
    }
}
