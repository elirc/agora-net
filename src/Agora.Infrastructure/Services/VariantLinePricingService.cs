using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed class VariantLinePricingService(AgoraDbContext db)
{
    public async Task<IReadOnlyDictionary<Guid, CalculatedVariantPrice>> CalculateAsync(IEnumerable<CartItem> items, CancellationToken ct = default)
    {
        var lines = items.ToArray();
        if (lines.Length == 0) return new Dictionary<Guid, CalculatedVariantPrice>();
        var ids = lines.Select(i => i.ProductVariantId).Distinct().ToArray();
        // One policy query for the whole cart, including saved lines; DTO mapping stays pure.
        var tiers = (await db.Set<VariantQuantityTier>().AsNoTracking().Where(t => ids.Contains(t.ProductVariantId)).ToListAsync(ct))
            .ToLookup(t => t.ProductVariantId);
        return lines.ToDictionary(i => i.Id, i => VariantPriceCalculator.Calculate(
            i.ProductVariant?.Price ?? throw new InvalidOperationException("Cart variants must be loaded before pricing."),
            i.Quantity, tiers[i.ProductVariantId]));
    }
}
