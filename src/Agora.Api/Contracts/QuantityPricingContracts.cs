using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record PutQuantityPricingRequest([property: JsonRequired] [Range(0, long.MaxValue)] long? ExpectedRevision,
    [Required, MaxLength(5)] List<QuantityTierInput> Tiers);
public sealed record QuantityPricingResponse(Guid VariantId, string Currency, decimal BaseUnitAmount, long? Revision,
    IReadOnlyList<QuantityTierInput> Tiers);
