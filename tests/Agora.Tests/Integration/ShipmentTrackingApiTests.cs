using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class ShipmentTrackingApiTests
{
    [Fact]
    public async Task Manual_progress_appends_one_event_per_revision_without_affecting_order_or_stock()
    {
        var providers = new CountingCheckoutProviders(); using var scenario = await ReportTestScenario.Create(providers.Register);
        using var owner = await AccountTestHelpers.Create(scenario, "tracking-owner"); using var other = await AccountTestHelpers.Create(scenario, "tracking-other");
        Order order = null!; Order secondOrder = null!; Fulfillment fulfillment = null!; int stock = 0;
        await scenario.Db(async db =>
        {
            order = await OperationalHistoryTestData.Order(db, owner.Id, scenario.Clock.Instant.AddDays(-1));
            secondOrder = await OperationalHistoryTestData.Order(db, owner.Id, scenario.Clock.Instant);
            fulfillment = OperationalHistoryTestData.Fulfillment(order, scenario.Clock.Instant.AddDays(-1)); db.Fulfillments.Add(fulfillment); await db.SaveChangesAsync();
            stock = (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == order.Items.Single().ProductVariantId)).QuantityOnHand;
        });
        var path = $"/api/admin/fulfillments/{fulfillment.Id}/tracking-events";
        var mine = $"/api/me/orders/{order.Number}/fulfillments/{fulfillment.Id}/tracking-events";
        var empty = (await scenario.Admin.GetFromJsonAsync<AdminShipmentTrackingHistoryResponse>(path))!;
        Assert.Equal(("Unknown", 0L), (empty.Status, empty.Version)); Assert.Empty(empty.Events.Items);
        var statuses = new[] { "InTransit", "Exception", "InTransit", "Delivered" };
        for (var i = 0; i < statuses.Length; i++)
        {
            var created = await scenario.Admin.PostAsJsonAsync(path, new AddShipmentTrackingRequest(i, statuses[i], "  Public carrier update  "));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var entry = (await created.Content.ReadFromJsonAsync<AdminShipmentTrackingEventResponse>())!;
            Assert.Equal(i + 1L, entry.Sequence); Assert.Equal(statuses[i], entry.Status);
            Assert.Equal("Public carrier update", entry.Message); Assert.Equal(scenario.Clock.Instant, entry.RecordedAt);
            Assert.NotEqual(Guid.Empty, entry.ActorAdminId);
        }
        var history = (await owner.Client.GetFromJsonAsync<ShipmentTrackingHistoryResponse>(mine + "?page=2&pageSize=2"))!;
        Assert.Equal(("Delivered", 4L), (history.Status, history.Version)); Assert.Equal(4, history.Events.TotalCount);
        Assert.Equal(new long[] { 3, 4 }, history.Events.Items.Select(e => e.Sequence));
        Assert.DoesNotContain("actorAdminId", await owner.Client.GetStringAsync(mine));
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PostAsJsonAsync(path, new AddShipmentTrackingRequest(4, "InTransit"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PostAsJsonAsync(path, new AddShipmentTrackingRequest(4, "Delivered"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PostAsJsonAsync(path, new AddShipmentTrackingRequest(3, "Exception"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(mine)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/me/orders/{secondOrder.Number}/fulfillments/{fulfillment.Id}/tracking-events")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.PostAsJsonAsync(path, new AddShipmentTrackingRequest(4, "Exception"))).StatusCode);
        using var anonymous = scenario.App.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(mine)).StatusCode);
        foreach (var query in new[] { "?page=0", "?pageSize=101", "?page=2147483647&pageSize=100" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.GetAsync(mine + query)).StatusCode);
        Assert.Equal((0, 0, 0), (providers.Charges, providers.Refunds, providers.Sends));
        await scenario.Db(async db =>
        {
            var actual = await db.Orders.SingleAsync(o => o.Id == order.Id);
            Assert.Equal((order.Status, order.FulfilledAt, order.Total), (actual.Status, actual.FulfilledAt, actual.Total));
            Assert.Equal(stock, (await db.InventoryItems.SingleAsync(i => i.ProductVariantId == order.Items.Single().ProductVariantId)).QuantityOnHand);
            Assert.Equal(4, await db.ShipmentTrackingEvents.CountAsync());
        });
    }

    [Fact]
    public async Task Invalid_named_states_messages_and_missing_revisions_are_rejected_before_any_event()
    {
        using var scenario = await ReportTestScenario.Create(); Fulfillment fulfillment = null!;
        await scenario.Db(async db =>
        {
            var order = await OperationalHistoryTestData.Order(db, null, scenario.Clock.Instant);
            fulfillment = OperationalHistoryTestData.Fulfillment(order, scenario.Clock.Instant); db.Fulfillments.Add(fulfillment); await db.SaveChangesAsync();
        });
        var path = $"/api/admin/fulfillments/{fulfillment.Id}/tracking-events";
        foreach (var status in new[] { "1", "InTransit, Exception", "UnknownValue" })
            Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync(path, new AddShipmentTrackingRequest(0, status))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync(path, new { status = "InTransit" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync(path, new AddShipmentTrackingRequest(-1, "InTransit"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync(path, new AddShipmentTrackingRequest(0, "InTransit", new string('x', 201)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PostAsJsonAsync(path, new AddShipmentTrackingRequest(0, "Delivered"))).StatusCode);
        var created = await scenario.Admin.PostAsJsonAsync(path, new AddShipmentTrackingRequest(0, "intransit", new string('x', 200)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PostAsJsonAsync(path, new AddShipmentTrackingRequest(1, "InTransit"))).StatusCode);
        await scenario.Db(async db => Assert.Single(await db.ShipmentTrackingEvents.ToListAsync()));
    }
}
