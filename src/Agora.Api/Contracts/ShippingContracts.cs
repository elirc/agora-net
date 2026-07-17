using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record ShippingMethodResponse(
    string Code,
    string Name,
    string RateType,
    decimal BaseRate,
    decimal PerKgRate,
    decimal? FreeThreshold,
    int MinDays,
    int MaxDays,
    bool IsActive,
    bool IsDefault)
{
    public static ShippingMethodResponse From(ShippingMethod method) => new(
        method.Code,
        method.Name,
        method.RateType.ToString(),
        method.BaseRate,
        method.PerKgRate,
        method.FreeThreshold,
        method.MinDays,
        method.MaxDays,
        method.IsActive,
        method.IsDefault);
}

public sealed record CreateShippingMethodRequest(
    [Required, MaxLength(64)] string Code,
    [Required, MaxLength(200)] string Name,
    [Required] string RateType,
    [Range(0, 100000)] decimal BaseRate,
    [Range(0, 100000)] decimal PerKgRate,
    [Range(0, 1000000)] decimal? FreeThreshold,
    [Range(0, 365)] int MinDays,
    [Range(0, 365)] int MaxDays,
    bool? IsActive,
    bool? IsDefault);

public sealed record UpdateShippingMethodRequest(
    [Required, MaxLength(200)] string Name,
    [Required] string RateType,
    [Range(0, 100000)] decimal BaseRate,
    [Range(0, 100000)] decimal PerKgRate,
    [Range(0, 1000000)] decimal? FreeThreshold,
    [Range(0, 365)] int MinDays,
    [Range(0, 365)] int MaxDays,
    bool IsActive,
    bool IsDefault);
