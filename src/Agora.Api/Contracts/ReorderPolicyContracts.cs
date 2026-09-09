using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Agora.Api.Contracts;

public sealed record ReplaceReorderPolicyRequest(
    [Required, Range(0, 1_000_000)] int? Threshold,
    [Required, Range(0, 1_000_000)] int? TargetLevel,
    [property: JsonRequired] long? ExpectedVersion) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Threshold > TargetLevel) yield return new ValidationResult("Threshold must not exceed targetLevel.", [nameof(Threshold)]);
        if (ExpectedVersion < 0) yield return new ValidationResult("ExpectedVersion must be null or nonnegative.", [nameof(ExpectedVersion)]);
    }
}
public sealed record ReorderPolicyResponse(Guid VariantId, bool HasOverride, int Threshold,
    int TargetLevel, long? Version, DateTimeOffset? UpdatedAt);
public sealed record ReorderReportRow(Guid VariantId, string Sku, string ProductName, string VariantName,
    int OnHand, int Reserved, long Available, bool HasOverride, int Threshold, int TargetLevel, long SuggestedQuantity);
