using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Agora.Api.Contracts;

public sealed record PutShippingEligibilityRequest([Required, MaxLength(50)] List<string> Countries,
    [Range(0, 1_000_000)] int? MaximumWeightGrams,
    [property: JsonRequired] [Range(0, long.MaxValue)] long? ExpectedRevision);
public sealed record ShippingEligibilityPolicyResponse(Guid ShippingMethodId, IReadOnlyList<string> Countries,
    int? MaximumWeightGrams, long? Revision);
public sealed record ShippingEligibilityPreviewRequest([Required, RegularExpression("^[A-Za-z]{2}$")] string Country,
    [Range(0, long.MaxValue)] long WeightGrams);
public sealed record EligibleShippingMethodResponse(Guid Id, string Code, string Name, int MinDays, int MaxDays);
