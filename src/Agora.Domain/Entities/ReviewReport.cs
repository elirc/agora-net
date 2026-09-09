using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum ReviewReportReason { Spam, Abuse, OffTopic }
public enum ReviewReportStatus { Open, Resolved, Dismissed }

public class ReviewReport
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ReviewId { get; private set; }
    public Review? Review { get; private set; }
    public Guid CustomerId { get; private set; }
    public ReviewReportReason Reason { get; private set; }
    public string? Comment { get; private set; }
    public ReviewReportStatus Status { get; private set; } = ReviewReportStatus.Open;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? ResolvedByAdminId { get; private set; }
    public string? ResolutionNote { get; private set; }
    public long Version { get; private set; }
    private ReviewReport() { }
    public ReviewReport(Guid reviewId, Guid reporter, ReviewReportReason reason, string? comment, DateTimeOffset now)
    {
        if (!Enum.IsDefined(reason)) throw new DomainException("Unknown report reason.");
        if (comment?.Length > 500) throw new DomainException("Report comment must be at most 500 characters.");
        ReviewId = reviewId; CustomerId = reporter; Reason = reason;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(); CreatedAt = now;
    }
    public void Resolve(ReviewReportStatus outcome, string? note, Guid actor, DateTimeOffset now)
    {
        if (Status != ReviewReportStatus.Open) throw new ReviewReportConflictException("A report can only be resolved once.");
        if (outcome is not (ReviewReportStatus.Resolved or ReviewReportStatus.Dismissed)) throw new DomainException("Outcome must be Resolved or Dismissed.");
        if (note?.Length > 500) throw new DomainException("Resolution note must be at most 500 characters.");
        var nextVersion = checked(Version + 1);
        Status = outcome; ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ResolvedByAdminId = actor; ResolvedAt = now; Version = nextVersion;
    }
}

public sealed class ReviewReportConflictException(string message) : DomainException(message);
