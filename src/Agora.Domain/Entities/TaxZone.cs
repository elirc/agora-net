namespace Agora.Domain.Entities;

/// <summary>
/// Product tax classification (e.g. standard, reduced, zero). Products without
/// a category use each zone's default rate.
/// </summary>
public class TaxCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Geographic tax jurisdiction. A zone matches a shipping address by country,
/// optionally narrowed to a region; region-specific zones win over
/// country-wide ones. No matching zone means no tax.
/// </summary>
public class TaxZone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>Region/state within the country; null covers the whole country.</summary>
    public string? Region { get; set; }

    /// <summary>Rate for products without a category override, e.g. 0.08.</summary>
    public decimal DefaultRate { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<TaxZoneRate> Rates { get; set; } = [];

    /// <summary>The rate for a product's tax category (default when uncategorized/unmapped).</summary>
    public decimal RateFor(Guid? taxCategoryId)
    {
        if (taxCategoryId is { } id
            && Rates.FirstOrDefault(r => r.TaxCategoryId == id) is { } overrideRate)
        {
            return overrideRate.Rate;
        }

        return DefaultRate;
    }
}

/// <summary>Per-category rate override within a tax zone.</summary>
public class TaxZoneRate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaxZoneId { get; set; }
    public TaxZone? TaxZone { get; set; }
    public Guid TaxCategoryId { get; set; }
    public TaxCategory? TaxCategory { get; set; }
    public decimal Rate { get; set; }
}
