using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
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
    bool IsActive,
    DateTimeOffset? StartsAt = null)
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
        discount.IsActive,
        discount.StartsAt);
}

public sealed record CreateDiscountRequest(
    [Required, MaxLength(64)] string Code,
    [Required] string Type, // "Percentage" | "FixedAmount"
    [Range(0.01, 1_000_000)] decimal Value,
    [RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO code.")]
    string? Currency,
    [property: JsonConverter(typeof(OffsetTimestampJsonConverter))] DateTimeOffset? ExpiresAt,
    [Range(1, int.MaxValue)] int? UsageLimit,
    bool? IsActive,
    [property: JsonConverter(typeof(OffsetTimestampJsonConverter))] DateTimeOffset? StartsAt = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context) => DiscountScheduleValidation.Validate(StartsAt, ExpiresAt);
}

public sealed record UpdateDiscountRequest(
    [property: JsonConverter(typeof(OffsetTimestampJsonConverter))] DateTimeOffset? ExpiresAt,
    [Range(1, int.MaxValue)] int? UsageLimit,
    [Required] bool IsActive,
    [property: JsonConverter(typeof(OffsetTimestampJsonConverter))] DateTimeOffset? StartsAt = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context) => DiscountScheduleValidation.Validate(StartsAt, ExpiresAt);
}

internal static class DiscountScheduleValidation
{
    internal static IEnumerable<ValidationResult> Validate(DateTimeOffset? start, DateTimeOffset? expiry)
    {
        if (start is not null && expiry is not null && start >= expiry)
            yield return new ValidationResult("StartsAt must precede ExpiresAt.", ["StartsAt", "ExpiresAt"]);
    }
}
