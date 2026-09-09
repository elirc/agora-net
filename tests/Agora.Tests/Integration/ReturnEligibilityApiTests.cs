using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agora.Tests.Integration;

public class ReturnEligibilityApiTests
{
    [Fact]
    public async Task Preview_and_creation_share_remaining_quantities_estimates_and_exact_window_without_refunding()
    {
        var providers = new CountingCheckoutProviders();
        using var scenario = await ReportTestScenario.Create(services => { providers.Register(services); services.Configure<ReturnPolicyOptions>(o => o.WindowDays = 30); });
        using var owner = await AccountTestHelpers.Create(scenario, "return-window");
        Order order = null!; var deadline = scenario.Clock.Instant.AddDays(1);
        await scenario.Db(async db =>
        {
            order = await OperationalHistoryTestData.Order(db, owner.Id, deadline.AddDays(-30));
            db.ReturnRequests.AddRange(OperationalHistoryTestData.Return(order, 1, deadline.AddDays(-2)),
                OperationalHistoryTestData.Return(order, 2, deadline.AddDays(-2), ReturnStatus.Approved),
                OperationalHistoryTestData.Return(order, 1, deadline.AddDays(-2), ReturnStatus.Rejected));
            await db.SaveChangesAsync();
        });
        var path = $"/api/me/orders/{order.Number}/return-eligibility";
        var request = new CreateReturnRequestDto(null, "Damaged", null, [new(order.Items.Single().Id, 1)]);
        scenario.Clock.Instant = deadline.AddTicks(-1); scenario.Commands.Statements.Clear();
        var preview = (await owner.Client.GetFromJsonAsync<ReturnEligibilityResult>(path))!;
        Assert.True(preview.Eligible); Assert.Equal(deadline, preview.Deadline); Assert.Equal(scenario.Clock.Instant, preview.EvaluatedAt);
        Assert.Equal(2, preview.Lines.Single().RemainingQuantity); Assert.Equal(38.88m, preview.Lines.Single().EstimatedRefund);
        Assert.DoesNotContain(scenario.Commands.Statements, sql => sql.Contains("INSERT INTO") || sql.Contains("UPDATE ") || sql.Contains("DELETE FROM"));
        Assert.Equal((0, 0, 0), (providers.Charges, providers.Refunds, providers.Sends));
        var created = await owner.Client.PostAsJsonAsync($"/api/orders/{order.Number}/returns", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var rma = (await created.Content.ReadFromJsonAsync<ReturnResponse>())!;
        Assert.Equal(19.44m, rma.RefundAmount); Assert.Equal(scenario.Clock.Instant, rma.CreatedAt);
        Assert.Equal(1, (await owner.Client.GetFromJsonAsync<ReturnEligibilityResult>(path))!.Lines.Single().RemainingQuantity);
        foreach (var instant in new[] { deadline, deadline.AddTicks(1) })
        {
            scenario.Clock.Instant = instant;
            var expired = (await owner.Client.GetFromJsonAsync<ReturnEligibilityResult>(path))!;
            Assert.False(expired.Eligible); Assert.Contains("ReturnWindowExpired", expired.Reasons);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsJsonAsync($"/api/orders/{order.Number}/returns", request)).StatusCode);
        }
        Assert.Equal(0, providers.Refunds);
        // An earlier valid request remains approvable after the creation window ends.
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.PostAsync($"/api/returns/{rma.Number}/approve", null)).StatusCode);
        Assert.Equal(1, providers.Refunds);
        await scenario.Db(async db => Assert.Equal(4, await db.ReturnRequests.CountAsync(r => r.OrderId == order.Id)));
    }

    [Fact]
    public async Task Missing_timestamp_partial_status_foreign_and_guest_orders_are_explicitly_handled()
    {
        using var scenario = await ReportTestScenario.Create(services => services.Configure<ReturnPolicyOptions>(o => o.WindowDays = 30));
        using var owner = await AccountTestHelpers.Create(scenario, "eligibility-owner"); using var other = await AccountTestHelpers.Create(scenario, "eligibility-other");
        Order missing = null!; Order partial = null!; Order guest = null!;
        await scenario.Db(async db =>
        {
            missing = await OperationalHistoryTestData.Order(db, owner.Id, scenario.Clock.Instant);
            db.Entry(missing).Property(o => o.FulfilledAt).CurrentValue = null; await db.SaveChangesAsync();
            partial = await OperationalHistoryTestData.Order(db, owner.Id, scenario.Clock.Instant, fulfilled: false);
            guest = await OperationalHistoryTestData.Order(db, null, scenario.Clock.Instant); guest.Email = owner.Email; await db.SaveChangesAsync();
        });
        var missingResult = (await owner.Client.GetFromJsonAsync<ReturnEligibilityResult>($"/api/me/orders/{missing.Number}/return-eligibility"))!;
        Assert.False(missingResult.Eligible); Assert.Contains("MissingFulfilledAt", missingResult.Reasons); Assert.Null(missingResult.Deadline);
        var partialResult = (await owner.Client.GetFromJsonAsync<ReturnEligibilityResult>($"/api/me/orders/{partial.Number}/return-eligibility"))!;
        Assert.Contains("OrderNotFulfilled", partialResult.Reasons);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.Client.PostAsJsonAsync($"/api/orders/{missing.Number}/returns",
            new CreateReturnRequestDto(null, "Damaged", null, [new(missing.Items.Single().Id, 1)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync($"/api/orders/{partial.Number}/returns",
            new CreateReturnRequestDto(null, "Damaged", null, [new(partial.Items.Single().Id, 1)]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/me/orders/{missing.Number}/return-eligibility")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/me/orders/{guest.Number}/return-eligibility")).StatusCode);
        using var anonymous = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/me/orders/{missing.Number}/return-eligibility")).StatusCode);
    }

    [Fact]
    public async Task Disabled_policy_preserves_old_returns_and_cancelled_or_rejected_requests_do_not_consume_quantity()
    {
        using var scenario = await ReportTestScenario.Create(); using var owner = await AccountTestHelpers.Create(scenario, "disabled-window"); Order order = null!;
        await scenario.Db(async db =>
        {
            order = await OperationalHistoryTestData.Order(db, owner.Id, scenario.Clock.Instant.AddYears(-2), 2);
            db.ReturnRequests.AddRange(OperationalHistoryTestData.Return(order, 1, scenario.Clock.Instant, ReturnStatus.Rejected),
                OperationalHistoryTestData.Return(order, 1, scenario.Clock.Instant, ReturnStatus.Cancelled)); await db.SaveChangesAsync();
        });
        var path = $"/api/me/orders/{order.Number}/return-eligibility";
        var result = (await owner.Client.GetFromJsonAsync<ReturnEligibilityResult>(path))!;
        Assert.True(result.Eligible); Assert.Null(result.Deadline); Assert.Equal(2, result.Lines.Single().RemainingQuantity);
        Assert.Equal(HttpStatusCode.Created, (await owner.Client.PostAsJsonAsync($"/api/orders/{order.Number}/returns",
            new CreateReturnRequestDto(null, "Damaged", null, [new(order.Items.Single().Id, 2)]))).StatusCode);
        var exhausted = (await owner.Client.GetFromJsonAsync<ReturnEligibilityResult>(path))!;
        Assert.False(exhausted.Eligible); Assert.Contains("NoRemainingQuantity", exhausted.Reasons); Assert.Equal(0m, exhausted.Lines.Single().EstimatedRefund);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Invalid_window_fails_application_startup(int days)
    {
        using var factory = new AgoraApiFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Configure<ReturnPolicyOptions>(o => o.WindowDays = days)));
        var error = Assert.Throws<OptionsValidationException>(() => app.CreateClient());
        Assert.Contains("WindowDays", error.Message);
    }
}
