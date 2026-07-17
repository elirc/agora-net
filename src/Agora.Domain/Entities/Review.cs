using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum ReviewStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}

/// <summary>
/// Verified-purchase product review: one per customer per product, moderated
/// before it becomes publicly visible. Editing sends it back to moderation.
/// </summary>
public class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid CustomerId { get; set; }

    public int Rating { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;

    public ReviewStatus Status { get; private set; } = ReviewStatus.Pending;
    public string? ModerationNote { get; private set; }
    public DateTimeOffset? ModeratedAt { get; private set; }

    /// <summary>Denormalized count of helpful votes; source of truth is <see cref="ReviewVote"/>.</summary>
    public int HelpfulCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    private Review()
    {
        // EF Core materialization.
    }

    public Review(Guid productId, Guid customerId, int rating, string? title, string body)
    {
        ProductId = productId;
        CustomerId = customerId;
        SetContent(rating, title, body);
    }

    /// <summary>Edits the review and sends it back to moderation.</summary>
    public void Edit(int rating, string? title, string body)
    {
        SetContent(rating, title, body);
        Status = ReviewStatus.Pending;
        ModerationNote = null;
        ModeratedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Approve(DateTimeOffset now)
    {
        Status = ReviewStatus.Approved;
        ModerationNote = null;
        ModeratedAt = now;
    }

    public void Reject(string? note, DateTimeOffset now)
    {
        Status = ReviewStatus.Rejected;
        ModerationNote = note;
        ModeratedAt = now;
    }

    private void SetContent(int rating, string? title, string body)
    {
        if (rating is < 1 or > 5)
        {
            throw new DomainException("Rating must be between 1 and 5.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new DomainException("Review body cannot be empty.");
        }

        Rating = rating;
        Title = title?.Trim() ?? string.Empty;
        Body = body.Trim();
    }
}

/// <summary>A customer's "helpful" vote on an approved review (one per customer).</summary>
public class ReviewVote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReviewId { get; set; }
    public Review? Review { get; set; }
    public Guid CustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
