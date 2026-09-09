using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class ReviewReportsApiTests
{
    [Fact]
    public async Task Reporting_and_resolution_leave_review_unchanged_and_keep_internal_data_off_public_routes()
    {
        using var scenario = await ReportTestScenario.Create();
        using var author = await AccountTestHelpers.Create(scenario, "review-author");
        using var reporter = await AccountTestHelpers.Create(scenario, "review-reporter");
        using var second = await AccountTestHelpers.Create(scenario, "review-second");
        Review review = null!;
        await scenario.Db(async db =>
        {
            var productId = (await db.Products.FirstAsync()).Id;
            review = new Review(productId, author.Id, 4, "Source title", new string('b', 240)); review.Approve(scenario.Clock.Instant);
            db.Reviews.Add(review); await db.SaveChangesAsync();
        });
        var path = $"/api/reviews/{review.Id}/reports";
        var created = await reporter.Client.PostAsJsonAsync(path, new CreateReviewReportRequest("Spam", "  My observation  "));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var json = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain(reporter.Id.ToString(), json); Assert.DoesNotContain("resolutionNote", json);
        var receipt = (await created.Content.ReadFromJsonAsync<ReviewReportReceipt>())!;
        Assert.Equal("My observation", receipt.Comment); Assert.Equal("Open", receipt.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await reporter.Client.PostAsJsonAsync(path, new CreateReviewReportRequest("Abuse"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await second.Client.PostAsJsonAsync(path, new CreateReviewReportRequest("OffTopic"))).StatusCode);
        const string queuePath = "/api/admin/review-reports";
        Assert.Equal(HttpStatusCode.Forbidden, (await reporter.Client.GetAsync(queuePath)).StatusCode);
        var queue = (await scenario.Admin.GetFromJsonAsync<PagedResult<ReviewReportAdminResponse>>(queuePath + "?status=Open&pageSize=1"))!;
        Assert.Equal(2, queue.TotalCount);
        var first = Assert.Single(queue.Items);
        Assert.Equal(200, first.ReviewExcerpt.Length); Assert.Equal("Approved", first.ReviewStatus);
        var next = (await scenario.Admin.GetFromJsonAsync<PagedResult<ReviewReportAdminResponse>>(queuePath + "?status=Open&pageSize=1&page=2"))!;
        var ids = new[] { first.Id, Assert.Single(next.Items).Id };
        Assert.Equal(ids.OrderBy(id => id.ToString(), StringComparer.Ordinal), ids);
        var resolutionPath = queuePath + "/" + receipt.Id + "/resolution";
        Assert.Equal(HttpStatusCode.Forbidden, (await reporter.Client.PutAsJsonAsync(resolutionPath, new ResolveReviewReportRequest(0, "Resolved", "SECRET-INTERNAL"))).StatusCode);
        var resolution = await scenario.Admin.PutAsJsonAsync(resolutionPath, new ResolveReviewReportRequest(0, "Resolved", "SECRET-INTERNAL"));
        resolution.EnsureSuccessStatusCode();
        var resolved = (await resolution.Content.ReadFromJsonAsync<ReviewReportResolutionResponse>())!;
        Assert.Equal(("Resolved", 1L), (resolved.Status, resolved.Version)); Assert.Equal(scenario.Clock.Instant, resolved.ResolvedAt);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PutAsJsonAsync(resolutionPath, new ResolveReviewReportRequest(0, "Dismissed"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PutAsJsonAsync(resolutionPath, new ResolveReviewReportRequest(1, "Dismissed"))).StatusCode);
        var open = (await scenario.Admin.GetFromJsonAsync<PagedResult<ReviewReportAdminResponse>>(queuePath + "?status=Open"))!;
        Assert.Equal(1, open.TotalCount); Assert.DoesNotContain(open.Items, r => r.Id == receipt.Id);
        var publicReviews = await reporter.Client.GetStringAsync($"/api/products/{review.ProductId}/reviews");
        Assert.DoesNotContain("SECRET-INTERNAL", publicReviews); Assert.DoesNotContain("My observation", publicReviews);
        await scenario.Db(async db =>
        {
            var original = await db.Reviews.SingleAsync(r => r.Id == review.Id);
            Assert.Equal(ReviewStatus.Approved, original.Status); Assert.Equal(review.Body, original.Body);
            Assert.Equal((await db.Customers.SingleAsync(c => c.Role == CustomerRole.Admin)).Id, resolved.ResolvedByAdminId);
        });
    }

    [Fact]
    public async Task Creation_and_resolution_bounds_are_named_only_and_review_deletion_cascades_reports()
    {
        using var scenario = await ReportTestScenario.Create();
        using var author = await AccountTestHelpers.Create(scenario, "report-author");
        using var reporter = await AccountTestHelpers.Create(scenario, "report-reader");
        Review approved = null!, pending = null!;
        await scenario.Db(async db =>
        {
            var ids = await db.Products.Take(2).Select(p => p.Id).ToArrayAsync();
            approved = new Review(ids[0], author.Id, 5, null, "Approved source"); approved.Approve(scenario.Clock.Instant);
            pending = new Review(ids[1], author.Id, 3, null, "Pending source");
            db.Reviews.AddRange(approved, pending); await db.SaveChangesAsync();
        });
        var path = $"/api/reviews/{approved.Id}/reports";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await author.Client.PostAsJsonAsync(path, new CreateReviewReportRequest("Spam"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await reporter.Client.PostAsJsonAsync($"/api/reviews/{pending.Id}/reports", new CreateReviewReportRequest("Spam"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reporter.Client.PostAsJsonAsync($"/api/reviews/{Guid.NewGuid()}/reports", new CreateReviewReportRequest("Spam"))).StatusCode);
        foreach (var reason in new[] { "0", "99", "Spam,Abuse", "Unknown" })
            Assert.Equal(HttpStatusCode.BadRequest, (await reporter.Client.PostAsJsonAsync(path, new CreateReviewReportRequest(reason))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reporter.Client.PostAsJsonAsync(path, new CreateReviewReportRequest("Spam", new string('c', 501)))).StatusCode);
        var created = await reporter.Client.PostAsJsonAsync(path, new CreateReviewReportRequest("spam", new string('c', 500))); created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<ReviewReportReceipt>())!.Id;
        var resolutionPath = $"/api/admin/review-reports/{id}/resolution";
        foreach (var invalid in new[] { new ResolveReviewReportRequest(null, "Resolved"), new(0, "1"), new(0, "Open"), new(0, "Resolved", new string('n', 501)) })
            Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.PutAsJsonAsync(resolutionPath, invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scenario.Admin.PutAsJsonAsync(resolutionPath, new ResolveReviewReportRequest(1, "Dismissed"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Admin.PutAsJsonAsync(resolutionPath, new ResolveReviewReportRequest(0, "Dismissed", new string('n', 500)))).StatusCode);
        foreach (var query in new[] { "status=0", "status=Unknown", "page=0", "pageSize=101", "page=2147483647&pageSize=100" })
            Assert.Equal(HttpStatusCode.BadRequest, (await scenario.Admin.GetAsync("/api/admin/review-reports?" + query)).StatusCode);
        using var visitor = scenario.App.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PostAsJsonAsync(path, new CreateReviewReportRequest("Spam"))).StatusCode);
        await scenario.Db(async db => { db.Reviews.Remove(await db.Reviews.SingleAsync(r => r.Id == approved.Id)); await db.SaveChangesAsync(); Assert.Empty(await db.ReviewReports.ToListAsync()); });
    }
}
