using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record CreateReviewReportRequest([Required] string Reason, [MaxLength(500)] string? Comment = null);
public sealed record ResolveReviewReportRequest([Required, Range(0, long.MaxValue)] long? ExpectedVersion,
    [Required] string Outcome, [MaxLength(500)] string? Note = null);
public sealed record ReviewReportReceipt(Guid Id, Guid ReviewId, string Reason, string? Comment, string Status, DateTimeOffset CreatedAt)
{
    public static ReviewReportReceipt From(ReviewReport report) => new(report.Id, report.ReviewId,
        report.Reason.ToString(), report.Comment, report.Status.ToString(), report.CreatedAt);
}
public sealed record ReviewReportAdminResponse(Guid Id, Guid ReviewId, Guid ReporterId, string Reason,
    string? Comment, string Status, DateTimeOffset CreatedAt, string ReviewExcerpt, string ReviewStatus,
    long Version, DateTimeOffset? ResolvedAt, Guid? ResolvedByAdminId, string? ResolutionNote);
public sealed record ReviewReportResolutionResponse(Guid Id, string Status, long Version,
    DateTimeOffset? ResolvedAt, Guid? ResolvedByAdminId, string? Note);
