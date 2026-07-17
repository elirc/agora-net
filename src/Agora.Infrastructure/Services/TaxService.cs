using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

/// <summary>A discounted line amount with its product's tax category.</summary>
public sealed record TaxLine(Guid? TaxCategoryId, decimal DiscountedAmount);

/// <summary>
/// Zone-based tax calculation replacing the fixed flat-rate calculator.
/// The shipping address picks a zone (region-specific beats country-wide,
/// no match means no tax) and each line is taxed at the zone's rate for the
/// product's tax category.
/// </summary>
public class TaxService(AgoraDbContext db)
{
    public async Task<Money> CalculateTaxAsync(
        Address shippingAddress,
        IReadOnlyList<TaxLine> lines,
        string currency,
        CancellationToken ct = default)
    {
        var zone = await ResolveZoneAsync(shippingAddress, ct);
        if (zone is null)
        {
            return Money.Zero(currency);
        }

        var tax = lines.Sum(line => line.DiscountedAmount * zone.RateFor(line.TaxCategoryId));
        return new Money(decimal.Round(tax, 2, MidpointRounding.AwayFromZero), currency);
    }

    /// <summary>Region-specific zones win over country-wide zones.</summary>
    public async Task<TaxZone?> ResolveZoneAsync(Address address, CancellationToken ct = default)
    {
        var country = address.Country.Trim().ToUpperInvariant();
        var region = address.Region.Trim().ToUpperInvariant();

        var zones = await db.TaxZones
            .AsNoTracking()
            .Include(z => z.Rates)
            .Where(z => z.IsActive && z.Country == country)
            .ToListAsync(ct);

        return zones.FirstOrDefault(z =>
                z.Region != null && z.Region.Trim().ToUpperInvariant() == region)
            ?? zones.FirstOrDefault(z => z.Region == null);
    }
}
