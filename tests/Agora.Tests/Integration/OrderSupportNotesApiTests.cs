using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class OrderSupportNotesApiTests
{
    [Fact]
    public async Task Immutable_notes_keep_server_attribution_stable_pages_and_private_content_boundaries()
    {
        var providers = new CountingCheckoutProviders(); using var scenario = await ReportTestScenario.Create(providers.Register);
        using var owner = await AccountTestHelpers.Create(scenario, "notes-owner"); using var second = await AccountTestHelpers.Create(scenario, "notes-admin");
        Order order = null!; Guid firstAdmin = default;
        await scenario.Db(async db =>
        {
            (await db.Customers.SingleAsync(c => c.Id == second.Id)).Role = CustomerRole.Admin;
            firstAdmin = (await db.Customers.SingleAsync(c => c.Email == AgoraDbSeeder.AdminEmail)).Id; await db.SaveChangesAsync();
            order = await OperationalHistoryTestData.Order(db, owner.Id, scenario.Clock.Instant);
        });
        second.Client.UseBearer(await TestAuth.LoginAsync(second.Client, second.Email, TestAuth.CustomerPassword));
        var marker = "PRIVATE-SUPPORT-" + Guid.NewGuid().ToString("N"); var path = $"/api/admin/orders/{order.Number}/notes";
        var firstResponse = await scenario.Admin.PostAsJsonAsync(path, new AddOrderSupportNoteRequest("  " + marker + " one  "));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = (await firstResponse.Content.ReadFromJsonAsync<OrderSupportNoteResponse>())!;
        var secondResponse = await second.Client.PostAsJsonAsync(path, new AddOrderSupportNoteRequest(marker + " two"));
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var last = (await secondResponse.Content.ReadFromJsonAsync<OrderSupportNoteResponse>())!;
        Assert.Equal(firstAdmin, first.AuthorAdminId); Assert.Equal(second.Id, last.AuthorAdminId);
        Assert.Equal(marker + " one", first.Body); Assert.Equal(scenario.Clock.Instant, first.CreatedAt); Assert.Equal(first.CreatedAt, last.CreatedAt);
        var expected = new[] { first, last }.OrderBy(n => n.Id).ToArray();
        for (var page = 1; page <= 2; page++)
        {
            var response = (await scenario.Admin.GetFromJsonAsync<PagedResult<OrderSupportNoteResponse>>($"{path}?page={page}&pageSize=1"))!;
            Assert.Equal(2, response.TotalCount); Assert.Equal(expected[page - 1], response.Items.Single());
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.PostAsJsonAsync(path, new AddOrderSupportNoteRequest("Customer"))).StatusCode);
        using var anonymous = scenario.App.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.DoesNotContain(marker, await owner.Client.GetStringAsync($"/api/orders/{order.Number}"));
        Assert.DoesNotContain(marker, await owner.Client.GetStringAsync("/api/me/orders"));
        Assert.DoesNotContain(marker, await owner.Client.GetStringAsync($"/api/me/orders/{order.Number}/timeline"));
        Assert.DoesNotContain(marker, await scenario.Admin.GetStringAsync($"/api/admin/orders/{order.Number}/packing-slip"));
        await scenario.Db(async db =>
        {
            var actual = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
            Assert.DoesNotContain(marker, JsonSerializer.Serialize(WebhookService.OrderPayload(actual)));
            Assert.Equal((order.Status, order.Total, order.PaidAt, order.FulfilledAt), (actual.Status, actual.Total, actual.PaidAt, actual.FulfilledAt));
            db.Customers.Remove(await db.Customers.SingleAsync(c => c.Id == second.Id)); await db.SaveChangesAsync();
        });
        var retained = (await scenario.Admin.GetFromJsonAsync<PagedResult<OrderSupportNoteResponse>>(path))!;
        Assert.Contains(retained.Items, n => n.AuthorAdminId == second.Id && n.Body == marker + " two");
        Assert.Equal((0, 0, 0), (providers.Charges, providers.Refunds, providers.Sends));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await scenario.Admin.PutAsJsonAsync(path, new AddOrderSupportNoteRequest("Edit"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await scenario.Admin.DeleteAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Pending_invalid_body_spoofed_actor_and_invalid_pages_fail_without_notes()
    {
        using var scenario = await ReportTestScenario.Create(); Order pending = null!; Order paid = null!;
        await scenario.Db(async db =>
        {
            pending = new Order { Number = "ORD-PENDING-NOTE", Email = "pending@example.test", ShippingAddress = CheckoutQuoteApiTests.Address.ToAddress() };
            db.Orders.Add(pending); await db.SaveChangesAsync();
            paid = await OperationalHistoryTestData.Order(db, null, scenario.Clock.Instant);
        });
        var path = $"/api/admin/orders/{paid.Number}/notes";
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PostAsJsonAsync($"/api/admin/orders/{pending.Number}/notes", new AddOrderSupportNoteRequest("Too early"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Admin.PostAsJsonAsync("/api/admin/orders/missing/notes", new AddOrderSupportNoteRequest("Missing"))).StatusCode);
        foreach (var body in new[] { "", "  ", new string('x', 1001) })
            Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync(path, new AddOrderSupportNoteRequest(body))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PostAsJsonAsync(path, new { body = "Spoof", authorAdminId = Guid.NewGuid(), createdAt = DateTimeOffset.UnixEpoch })).StatusCode);
        foreach (var query in new[] { "?page=0", "?pageSize=101", "?page=2147483647&pageSize=100" })
            Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.GetAsync(path + query)).StatusCode);
        await scenario.Db(async db => Assert.Empty(await db.OrderSupportNotes.ToListAsync()));
        Assert.Equal(HttpStatusCode.Created, (await scenario.Admin.PostAsJsonAsync(path, new AddOrderSupportNoteRequest(new string('x', 1000)))).StatusCode);
    }
}
