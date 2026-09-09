using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class ReturnEvidenceApiTests
{
    [Fact]
    public async Task Evidence_is_owned_bounded_supplementary_and_available_after_approval_without_side_effects()
    {
        var providers = new CountingCheckoutProviders(); using var scenario = await ReportTestScenario.Create(providers.Register);
        using var owner = await AccountTestHelpers.Create(scenario, "evidence-owner"); using var other = await AccountTestHelpers.Create(scenario, "evidence-other");
        ReturnRequest approved = null!; ReturnRequest second = null!; ReturnRequest guest = null!;
        await scenario.Db(async db =>
        {
            var order = await OperationalHistoryTestData.Order(db, owner.Id, scenario.Clock.Instant.AddDays(-3));
            approved = OperationalHistoryTestData.Return(order, 1, scenario.Clock.Instant.AddDays(-2), ReturnStatus.Approved);
            second = OperationalHistoryTestData.Return(order, 1, scenario.Clock.Instant.AddDays(-1));
            var guestOrder = await OperationalHistoryTestData.Order(db, null, scenario.Clock.Instant); guestOrder.Email = owner.Email;
            guest = OperationalHistoryTestData.Return(guestOrder, 1, scenario.Clock.Instant);
            db.ReturnRequests.AddRange(approved, second, guest); await db.SaveChangesAsync();
        });
        var path = $"/api/me/returns/{approved.Number}/evidence";
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var created = await owner.Client.PostAsJsonAsync(path, new AddReturnEvidenceRequest($"https://example.test/evidence/{i}", "  Supplemental after approval  "));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var evidence = (await created.Content.ReadFromJsonAsync<ReturnEvidenceResponse>())!; ids.Add(evidence.Id);
            Assert.Equal(owner.Id, evidence.AuthorCustomerId); Assert.Equal(scenario.Clock.Instant, evidence.CreatedAt);
            Assert.Equal("Supplemental after approval", evidence.Description); Assert.True(evidence.CreatedAt > approved.ProcessedAt);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(path, new AddReturnEvidenceRequest("https://example.test/sixth"))).StatusCode);
        var mine = (await owner.Client.GetFromJsonAsync<ReturnEvidenceResponse[]>(path))!;
        Assert.Equal(ids.Order(), mine.Select(e => e.Id));
        var admin = (await scenario.Admin.GetFromJsonAsync<ReturnEvidenceResponse[]>($"/api/admin/returns/{approved.Number}/evidence"))!;
        Assert.Equal(mine, admin);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync(path, new AddReturnEvidenceRequest("https://example.test/foreign"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.DeleteAsync($"{path}/{ids[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/me/returns/{guest.Number}/evidence")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.DeleteAsync($"/api/me/returns/{second.Number}/evidence/{ids[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.DeleteAsync($"{path}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.GetAsync($"/api/admin/returns/{approved.Number}/evidence")).StatusCode);
        using var anonymous = scenario.App.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"{path}/{ids[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.Client.PostAsJsonAsync(path, new AddReturnEvidenceRequest("https://example.test/replacement"))).StatusCode);
        var ordinary = await owner.Client.GetStringAsync($"/api/returns/{approved.Number}");
        Assert.DoesNotContain("example.test/evidence", ordinary); Assert.DoesNotContain("Supplemental after approval", ordinary);
        Assert.Equal((0, 0, 0), (providers.Charges, providers.Refunds, providers.Sends));
        await scenario.Db(async db =>
        {
            var actual = await db.ReturnRequests.SingleAsync(r => r.Id == approved.Id);
            Assert.Equal((approved.Status, approved.RefundAmount, approved.ProcessedAt, approved.RefundTransactionId),
                (actual.Status, actual.RefundAmount, actual.ProcessedAt, actual.RefundTransactionId));
            Assert.Equal(OrderStatus.Fulfilled, (await db.Orders.SingleAsync(o => o.Id == approved.OrderId)).Status);
        });
    }

    [Fact]
    public async Task Link_validation_rejects_unsafe_shapes_and_client_authorship_fields_without_fetching_links()
    {
        using var scenario = await ReportTestScenario.Create(); using var owner = await AccountTestHelpers.Create(scenario, "evidence-validation"); ReturnRequest rma = null!;
        await scenario.Db(async db =>
        {
            var order = await OperationalHistoryTestData.Order(db, owner.Id, scenario.Clock.Instant);
            rma = OperationalHistoryTestData.Return(order, 1, scenario.Clock.Instant); db.ReturnRequests.Add(rma); await db.SaveChangesAsync();
        });
        var path = $"/api/me/returns/{rma.Number}/evidence";
        foreach (var url in new[] { "http://example.test/image", "/relative", "https://user:password@example.test/image", "https:///", "javascript:alert(1)", "https://example.test/" + new string('x', 2000) })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync(path, new AddReturnEvidenceRequest(url))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync(path, new AddReturnEvidenceRequest("https://example.test/image", new string('x', 201)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync(path, new { url = "https://example.test/image", authorCustomerId = Guid.NewGuid(), createdAt = DateTimeOffset.UnixEpoch })).StatusCode);
        var valid = await owner.Client.PostAsJsonAsync(path, new AddReturnEvidenceRequest("https://example.test/" + new string('x', 1979), new string('x', 200)));
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        Assert.Single((await owner.Client.GetFromJsonAsync<ReturnEvidenceResponse[]>(path))!);
    }
}
