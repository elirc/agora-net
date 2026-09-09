using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Agora.Api.Contracts;

public sealed record CheckoutPreferenceResponse(Guid? ShippingAddressId, string? ShippingMethodCode, long? Version);
public sealed record PutCheckoutPreferenceRequest(Guid? ShippingAddressId,
    [MaxLength(64)] string? ShippingMethodCode, [property: JsonRequired] [Range(0, long.MaxValue)] long? ExpectedVersion);
