using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record IssueGiftCardRequest(
    [Range(0.01, 100_000)] decimal Amount,
    [RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO code.")]
    string? Currency,
    DateTimeOffset? ExpiresAt);

public sealed record GiftCardResponse(
    string Code,
    string Currency,
    decimal InitialBalance,
    decimal Balance,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt)
{
    public static GiftCardResponse From(GiftCard card) => new(
        card.Code,
        card.Currency,
        card.InitialBalance,
        card.Balance,
        card.IsActive,
        card.ExpiresAt,
        card.CreatedAt);
}
