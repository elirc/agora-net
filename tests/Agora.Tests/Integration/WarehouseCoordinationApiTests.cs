using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public sealed class WarehouseCoordinationApiTests(AgoraApiFactory factory)
    : IClassFixture<AgoraApiFactory>, IAsyncLifetime
{
    private readonly HttpClient admin = factory.CreateClient();
    public Task InitializeAsync() => admin.AuthenticateAsAdminAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Hold_blocks_future_fulfillment_until_revision_protected_release()
    {
        var order = await CreatePaidOrder(3);
        var holdResult = await admin.PostAsJsonAsync($"/api/admin/orders/{order.Number}/holds",
            new CreateOrderHoldRequest("AddressQuestion", "Confirm apartment"));
        Assert.Equal(HttpStatusCode.Created, holdResult.StatusCode);
        var hold = (await holdResult.Content.ReadFromJsonAsync<OrderHoldResponse>())!;

        var publicOrderJson = await (await admin.GetAsync($"/api/orders/{order.Number}"))
            .Content.ReadAsStringAsync();
        Assert.DoesNotContain("Confirm apartment", publicOrderJson, StringComparison.Ordinal);

        var heldQueue = await admin.GetFromJsonAsync<PagedResult<FulfillmentQueueOrderResponse>>(
            "/api/admin/fulfillment-queue?held=true");
        Assert.Contains(heldQueue!.Items, row => row.Number == order.Number && row.IsHeld);
        var unheldQueue = await admin.GetFromJsonAsync<PagedResult<FulfillmentQueueOrderResponse>>(
            "/api/admin/fulfillment-queue?held=false");
        Assert.DoesNotContain(unheldQueue!.Items, row => row.Number == order.Number);

        var blocked = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest(null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Empty(await ReadFulfillments(order.Id));
        await factory.WithDbAsync(async db =>
        {
            var unchanged = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
            Assert.Equal("historical-payment", unchanged.PaymentTransactionId);
            Assert.Equal(OrderStatus.PartiallyFulfilled, unchanged.Status);
        });

        var stale = await admin.PostAsJsonAsync(
            $"/api/admin/orders/{order.Number}/holds/{hold.Id}/release", new ReleaseOrderHoldRequest(9));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        (await admin.PostAsJsonAsync($"/api/admin/orders/{order.Number}/holds/{hold.Id}/release",
            new ReleaseOrderHoldRequest(hold.Revision))).EnsureSuccessStatusCode();

        var fulfilled = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest(null, null, null));
        Assert.Equal(HttpStatusCode.Created, fulfilled.StatusCode);
    }

    [Fact]
    public async Task Assignment_cannot_be_bypassed_and_full_fulfillment_clears_slot()
    {
        var order = await CreatePaidOrder(1);
        var claimResult = await admin.PostAsync(
            $"/api/admin/orders/{order.Number}/work-assignment", null);
        var claim = (await claimResult.Content.ReadFromJsonAsync<WarehouseAssignmentResponse>())!;

        var missing = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest(null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        var wrong = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest(null, null, null, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);

        var allowed = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest(null, null, null, claim.AssignmentId));
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        Assert.Null(await ReadAssignment(order.Id));
    }

    [Fact]
    public async Task Legacy_fulfill_route_uses_structured_assignment_body()
    {
        var order = await CreatePaidOrder(1);
        var claim = (await (await admin.PostAsync(
            $"/api/admin/orders/{order.Number}/work-assignment", null))
            .Content.ReadFromJsonAsync<WarehouseAssignmentResponse>())!;

        var emptyBody = await admin.PostAsync($"/api/orders/{order.Number}/fulfill", null);
        Assert.Equal(HttpStatusCode.Conflict, emptyBody.StatusCode);
        var assigned = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfill",
            new LegacyFulfillRequest(claim.AssignmentId));
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.Null(await ReadAssignment(order.Id));
    }

    [Fact]
    public async Task Partial_fulfillment_keeps_assignment_for_more_work()
    {
        var order = await CreatePaidOrder(2);
        var claim = (await (await admin.PostAsync(
            $"/api/admin/orders/{order.Number}/work-assignment", null))
            .Content.ReadFromJsonAsync<WarehouseAssignmentResponse>())!;
        var response = await admin.PostAsJsonAsync($"/api/orders/{order.Number}/fulfillments",
            new CreateFulfillmentRequest(null, null,
                [new FulfillmentLineDto(order.Items.Single().Id, 1)], claim.AssignmentId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(claim.AssignmentId, (await ReadAssignment(order.Id))!.AssignmentId);
    }

    private async Task<Order> CreatePaidOrder(int quantity)
    {
        Order? result = null;
        await factory.WithDbAsync(async db =>
        {
            result = await OperationalHistoryTestData.Order(db, owner: null, fulfilledAt: DateTimeOffset.UtcNow,
                quantity, fulfilled: false);
        });
        return result!;
    }

    private async Task<Fulfillment[]> ReadFulfillments(Guid orderId)
    {
        Fulfillment[] rows = [];
        await factory.WithDbAsync(async db => rows = await db.Fulfillments
            .Where(x => x.OrderId == orderId).ToArrayAsync());
        return rows;
    }

    private async Task<WarehouseAssignment?> ReadAssignment(Guid orderId)
    {
        WarehouseAssignment? row = null;
        await factory.WithDbAsync(async db => row = await db.Set<WarehouseAssignment>()
            .AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId));
        return row;
    }
}
