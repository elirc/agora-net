using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record DiscountResponse(
    Guid Id,
    string Code,
    string Type,
    decimal Value,
    string Currency,
    DateTimeOffset? ExpiresAt,
    int? UsageLimit,
    int TimesUsed,
    bool IsActive)
{
    public static DiscountResponse From(DiscountCode discount) => new(
        discount.Id,
        discount.Code,
        discount.Type.ToString(),
        discount.Value,
        discount.Currency,
        discount.ExpiresAt,
        discount.UsageLimit,
        discount.TimesUsed,
        discount.IsActive);
}

public sealed record CreateDiscountRequest(
    [Required, MaxLength(64)] string Code,
    [Required] string Type, // "Percentage" | "FixedAmount"
    [Range(0.01, 1_000_000)] decimal Value,
    [RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO code.")]
    string? Currency,
    DateTimeOffset? ExpiresAt,
    [Range(1, int.MaxValue)] int? UsageLimit,
    bool? IsActive);

public sealed record UpdateDiscountRequest(
    DateTimeOffset? ExpiresAt,
    [Range(1, int.MaxValue)] int? UsageLimit,
    [Required] bool IsActive);
