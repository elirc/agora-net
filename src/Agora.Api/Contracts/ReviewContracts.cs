using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record CreateReviewRequest(
    [Range(1, 5)] int Rating,
    [MaxLength(200)] string? Title,
    [Required, MaxLength(4000)] string Body);

public sealed record RejectReviewRequest(
    [MaxLength(500)] string? Note);

public sealed record ReviewResponse(
    Guid Id,
    Guid ProductId,
    string ReviewerName,
    int Rating,
    string Title,
    string Body,
    string Status,
    string? ModerationNote,
    int HelpfulCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ReviewResponse From(Review review, string reviewerName) => new(
        review.Id,
        review.ProductId,
        reviewerName,
        review.Rating,
        review.Title,
        review.Body,
        review.Status.ToString(),
        review.ModerationNote,
        review.HelpfulCount,
        review.CreatedAt,
        review.UpdatedAt);

    /// <summary>Masks an email to a display name, e.g. "ada@example.com" -> "ada".</summary>
    public static string DisplayName(string fullName, string email) =>
        !string.IsNullOrWhiteSpace(fullName)
            ? fullName
            : email.Split('@')[0];
}
