using System.Text.Json;
using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public class ShippingEligibilityPolicy
{
    public Guid ShippingMethodId { get; private set; }
    public string AllowedCountriesJson { get; private set; } = "[]";
    public int? MaximumWeightGrams { get; private set; }
    public long Revision { get; private set; }
    private ShippingEligibilityPolicy() { }
    public ShippingEligibilityPolicy(Guid methodId, IReadOnlyList<string> countries, int? maximumWeightGrams)
    { ShippingMethodId = methodId; ReplaceCore(countries, maximumWeightGrams); }
    public IReadOnlyList<string> Countries() => JsonSerializer.Deserialize<string[]>(AllowedCountriesJson) ?? [];
    public void Replace(IReadOnlyList<string> countries, int? maximumWeightGrams)
    { var next = checked(Revision + 1); ReplaceCore(countries, maximumWeightGrams); Revision = next; }
    private void ReplaceCore(IReadOnlyList<string> countries, int? maximumWeightGrams)
    {
        if (maximumWeightGrams is < 0 or > 1_000_000) throw new DomainException("Maximum weight must be 0..1,000,000 grams.");
        if (countries.Count > 50) throw new DomainException("At most 50 destination countries are allowed.");
        var normalized = countries.Select(c => (c ?? "").Trim().ToUpperInvariant()).ToArray();
        if (normalized.Any(c => c.Length != 2 || c.Any(ch => ch is < 'A' or > 'Z')))
            throw new DomainException("Countries must be two-letter ASCII codes.");
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new DomainException("Countries must be unique after normalization.");
        AllowedCountriesJson = JsonSerializer.Serialize(normalized.Order(StringComparer.Ordinal)); MaximumWeightGrams = maximumWeightGrams;
    }
}

public sealed record ShippingEligibilityResult(bool Eligible, IReadOnlyList<string> Reasons);

public static class ShippingEligibilityRules
{
    public static ShippingEligibilityResult Evaluate(IReadOnlyList<string> allowedCountries, int? maximumWeightGrams,
        string country, long weightGrams)
    {
        if (weightGrams < 0) throw new DomainException("Shipment weight cannot be negative.");
        var normalized = (country ?? "").Trim().ToUpperInvariant();
        if (normalized.Length != 2 || normalized.Any(ch => ch is < 'A' or > 'Z')) throw new DomainException("Country must be a two-letter ASCII code.");
        var reasons = new List<string>();
        if (allowedCountries.Count > 0 && !allowedCountries.Contains(normalized, StringComparer.Ordinal)) reasons.Add("CountryNotServed");
        if (maximumWeightGrams is { } maximum && weightGrams > maximum) reasons.Add("WeightExceeded");
        return new(reasons.Count == 0, reasons);
    }
}
