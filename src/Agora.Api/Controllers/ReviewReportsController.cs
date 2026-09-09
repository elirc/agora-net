using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize]
[Agora.Api.Filters.LocalSqliteWrite]
[Route("api")]
public class ReviewReportsController(AgoraDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPost("reviews/{reviewId:guid}/reports")]
    public async Task<ActionResult<ReviewReportReceipt>> Create(Guid reviewId, CreateReviewReportRequest request, CancellationToken ct)
    {
        var reporter = User.GetCustomerId(); if (reporter is null) return Unauthorized();
        if (!QueryRules.TryNamedEnum<ReviewReportReason>(request.Reason, out var reason))
            return BadRequest(new ProblemDetails { Title = "Reason must be Spam, Abuse, or OffTopic." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var review = await db.Reviews.AsNoTracking().Where(r => r.Id == reviewId)
            .Select(r => new { r.CustomerId, r.Status }).SingleOrDefaultAsync(ct);
        if (review is null) return NotFound();
        if (review.CustomerId == reporter || review.Status != ReviewStatus.Approved)
            return UnprocessableEntity(new ProblemDetails { Title = "Only another customer's approved review can be reported." });
        if (await db.ReviewReports.AnyAsync(r => r.ReviewId == reviewId && r.CustomerId == reporter, ct))
            return Conflict(new ProblemDetails { Title = "You have already reported this review." });
        var report = new ReviewReport(reviewId, reporter.Value, reason, request.Comment, clock.GetUtcNow());
        db.ReviewReports.Add(report);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        { return Conflict(new ProblemDetails { Title = "You have already reported this review." }); }
        await transaction.CommitAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return StatusCode(StatusCodes.Status201Created, ReviewReportReceipt.From(report));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin/review-reports")]
    public async Task<ActionResult<PagedResult<ReviewReportAdminResponse>>> List([FromQuery] string? status = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!QueryRules.ValidPage(page, pageSize)) return BadRequest(new ProblemDetails { Title = "Invalid pagination." });
        ReviewReportStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!QueryRules.TryNamedEnum<ReviewReportStatus>(status, out var parsed)) return BadRequest(new ProblemDetails { Title = "Unknown report status." });
            filter = parsed;
        }
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var query = db.ReviewReports.AsNoTracking().Where(r => !filter.HasValue || r.Status == filter);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new ReviewReportAdminResponse(r.Id, r.ReviewId, r.CustomerId, r.Reason.ToString(), r.Comment,
                r.Status.ToString(), r.CreatedAt, r.Review!.Body.Length > 200 ? r.Review.Body.Substring(0, 200) : r.Review.Body,
                r.Review.Status.ToString(), r.Version, r.ResolvedAt, r.ResolvedByAdminId, r.ResolutionNote)).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new PagedResult<ReviewReportAdminResponse>(rows, page, pageSize, total));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("admin/review-reports/{id:guid}/resolution")]
    public async Task<ActionResult<ReviewReportResolutionResponse>> Resolve(Guid id, ResolveReviewReportRequest request, CancellationToken ct)
    {
        var actor = User.GetCustomerId(); if (actor is null) return Unauthorized();
        if (!QueryRules.TryNamedEnum<ReviewReportStatus>(request.Outcome, out var outcome) || outcome == ReviewReportStatus.Open)
            return BadRequest(new ProblemDetails { Title = "Outcome must be Resolved or Dismissed." });
        var report = await db.ReviewReports.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (report is null) return NotFound();
        if (report.Version != request.ExpectedVersion || report.Status != ReviewReportStatus.Open)
            return Conflict(new ProblemDetails { Title = "The report changed or was already resolved." });
        report.Resolve(outcome, request.Note, actor.Value, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(new ReviewReportResolutionResponse(report.Id, report.Status.ToString(), report.Version,
            report.ResolvedAt, report.ResolvedByAdminId, report.ResolutionNote));
    }
}
