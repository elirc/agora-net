using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class BulkInventoryAdjustmentApiTests
{
    [Fact]
    public async Task Batch_returns_actual_snapshots_and_normalized_replay_survives_old_versions_and_catalog_changes()
    {
        using var scenario = await ReportTestScenario.Create();
        var lines = await Arrange(scenario);
        string sku = "";
        await scenario.Db(async db => sku = (await db.ProductVariants.SingleAsync(v => v.Id == lines[0].VariantId)).Sku);
        var observation = (await scenario.Admin.GetFromJsonAsync<InventoryResponse>("/api/inventory/" + Uri.EscapeDataString(sku)))!;
        Assert.Equal(lines[0].VariantId, observation.ProductVariantId);
        Assert.Equal(lines[0].ExpectedVersion, observation.Version);
        var operationId = Guid.NewGuid();
        var request = new InventoryAdjustmentRequest(operationId, "  Cycle count  ", lines);
        const string path = "/api/admin/inventory/adjustments";
        var response = await scenario.Admin.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<InventoryAdjustmentResponse>())!;
        Assert.Equal(operationId, receipt.OperationId);
        Assert.Equal("Cycle count", receipt.Reason);
        Assert.Equal(scenario.Clock.Instant, receipt.CreatedAt);
        await scenario.Db(async db => Assert.Equal((await db.Customers.SingleAsync(c => c.Email == AgoraDbSeeder.AdminEmail)).Id, receipt.ActorId));
        var a = receipt.Lines.Single(l => l.VariantId == lines[0].VariantId);
        var b = receipt.Lines.Single(l => l.VariantId == lines[1].VariantId);
        Assert.Equal((10, 7, 2), (a.BeforeOnHand, a.AfterOnHand, a.Reserved));
        Assert.Equal((8, 12, 0), (b.BeforeOnHand, b.AfterOnHand, b.Reserved));
        Assert.Equal(a.BeforeVersion + 1, a.AfterVersion);
        Assert.Equal(b.BeforeVersion + 1, b.AfterVersion);
        var read = await scenario.Admin.GetAsync(response.Headers.Location);
        read.EnsureSuccessStatusCode();
        Assert.Equal(await response.Content.ReadAsStringAsync(), await read.Content.ReadAsStringAsync());
        var replay = await scenario.Admin.PostAsJsonAsync(path, request with { Reason = "Cycle count", Lines = lines.AsEnumerable().Reverse().ToList() });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PostAsJsonAsync(path, request with { Reason = "Different count" })).StatusCode);
        var changed = lines.Select(l => l with { ExpectedVersion = l.ExpectedVersion + 1 }).ToList();
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PostAsJsonAsync(path, request with { Lines = changed })).StatusCode);
        await scenario.Db(async db =>
        {
            Assert.Equal(1, await db.InventoryAdjustmentBatches.CountAsync());
            Assert.Equal(2, await db.InventoryAdjustmentLines.CountAsync());
            Assert.Equal(7, (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == lines[0].VariantId)).QuantityOnHand);
            Assert.Equal(12, (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == lines[1].VariantId)).QuantityOnHand);
            db.ProductVariants.Remove(await db.ProductVariants.SingleAsync(v => v.Id == lines[0].VariantId));
            await db.SaveChangesAsync();
        });
        var afterDeletion = await scenario.Admin.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, afterDeletion.StatusCode);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await afterDeletion.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Every_invalid_or_stale_line_rejects_the_entire_operation_and_access_is_admin_only()
    {
        using var scenario = await ReportTestScenario.Create();
        var lines = await Arrange(scenario);
        const string path = "/api/admin/inventory/adjustments";
        var operationId = Guid.NewGuid();
        InventoryAdjustmentRequest Request(List<InventoryAdjustmentLineRequest> value) => new(operationId, "Count correction", value);
        var invalid = new List<(InventoryAdjustmentRequest Request, HttpStatusCode Status)>
        {
            (Request([lines[0] with { Delta = -9 }, lines[1]]), HttpStatusCode.UnprocessableEntity),
            (Request([lines[0], lines[1] with { ExpectedVersion = lines[1].ExpectedVersion + 1 }]), HttpStatusCode.Conflict),
            (Request([lines[0], lines[1] with { VariantId = Guid.NewGuid() }]), HttpStatusCode.NotFound),
            (Request([lines[0], lines[0]]), HttpStatusCode.BadRequest),
            (Request([lines[0] with { Delta = 0 }]), HttpStatusCode.BadRequest),
            (Request([lines[0] with { Delta = 1_000_000 }]), HttpStatusCode.UnprocessableEntity),
            (Request([lines[0] with { Delta = 1_000_001 }]), HttpStatusCode.BadRequest),
            (Request([lines[0] with { ExpectedVersion = null }]), HttpStatusCode.BadRequest),
            (Request(lines) with { Reason = "  " }, HttpStatusCode.BadRequest),
            (Request(lines) with { Reason = new string('x', 201) }, HttpStatusCode.BadRequest),
            (Request(lines) with { OperationId = Guid.Empty }, HttpStatusCode.BadRequest),
            (Request([]), HttpStatusCode.BadRequest),
        };
        foreach (var (request, status) in invalid)
            Assert.Equal(status, (await scenario.Admin.PostAsJsonAsync(path, request)).StatusCode);
        using var raw = new StringContent("{\"operationId\":\"" + operationId + "\",\"reason\":\"count\",\"lines\":[null]}", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsync(path, raw)).StatusCode);
        await scenario.Db(async db =>
        {
            Assert.Empty(await db.InventoryAdjustmentBatches.ToListAsync());
            Assert.Empty(await db.InventoryAdjustmentLines.ToListAsync());
            var a = await db.InventoryItems.SingleAsync(i => i.ProductVariantId == lines[0].VariantId);
            var b = await db.InventoryItems.SingleAsync(i => i.ProductVariantId == lines[1].VariantId);
            Assert.Equal((10, 8), (a.QuantityOnHand, b.QuantityOnHand));
            Assert.Equal(lines[0].ExpectedVersion, a.Version);
            Assert.Equal(lines[1].ExpectedVersion, b.Version);
        });
        using var visitor = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PostAsJsonAsync(path, Request(lines))).StatusCode);
        visitor.UseBearer(await TestAuth.RegisterAsync(visitor, $"batch-{Guid.NewGuid():N}@example.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.PostAsJsonAsync(path, Request(lines))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await visitor.GetAsync(path + "/" + operationId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.GetAsync(path + "/" + operationId)).StatusCode);
    }

    private static async Task<List<InventoryAdjustmentLineRequest>> Arrange(ReportTestScenario scenario)
    {
        List<InventoryAdjustmentLineRequest> lines = [];
        await scenario.Db(async db =>
        {
            var stock = await db.InventoryItems.OrderBy(i => i.ProductVariantId).Take(2).ToListAsync();
            stock[0].SetStock(10); stock[0].Reserve(2); stock[1].SetStock(8);
            await db.SaveChangesAsync();
            lines = [new(stock[0].ProductVariantId, -3, stock[0].Version), new(stock[1].ProductVariantId, 4, stock[1].Version)];
        });
        return lines;
    }
}
