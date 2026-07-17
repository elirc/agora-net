using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record TaxCategoryResponse(Guid Id, string Code, string Name)
{
    public static TaxCategoryResponse From(TaxCategory category) =>
        new(category.Id, category.Code, category.Name);
}

public sealed record CreateTaxCategoryRequest(
    [Required, MaxLength(64)] string Code,
    [Required, MaxLength(200)] string Name);

public sealed record TaxZoneRateDto(
    [Required, MaxLength(64)] string TaxCategoryCode,
    [Range(0, 1)] decimal Rate);

public sealed record TaxZoneRateResponse(string TaxCategoryCode, decimal Rate);

public sealed record TaxZoneResponse(
    Guid Id,
    string Code,
    string Name,
    string Country,
    string? Region,
    decimal DefaultRate,
    bool IsActive,
    IReadOnlyList<TaxZoneRateResponse> Rates)
{
    /// <summary>Maps a zone whose rates (and their categories) are loaded.</summary>
    public static TaxZoneResponse From(TaxZone zone) => new(
        zone.Id,
        zone.Code,
        zone.Name,
        zone.Country,
        zone.Region,
        zone.DefaultRate,
        zone.IsActive,
        zone.Rates
            .Select(r => new TaxZoneRateResponse(r.TaxCategory?.Code ?? string.Empty, r.Rate))
            .ToList());
}

public sealed record SaveTaxZoneRequest(
    [Required, MaxLength(64)] string Code,
    [Required, MaxLength(200)] string Name,
    [Required, RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Country must be a 2-letter ISO code.")]
    string Country,
    [MaxLength(100)] string? Region,
    [Range(0, 1)] decimal DefaultRate,
    bool? IsActive,
    List<TaxZoneRateDto>? Rates);
