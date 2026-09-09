using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Domain.Services;

public sealed record CalculatedVariantPrice(Money BasePrice, Money AppliedPrice, int? MinimumQuantity);

public static class VariantPriceCalculator
{
    public static CalculatedVariantPrice Calculate(Money basePrice, int quantity, IEnumerable<VariantQuantityTier> tiers)
    {
        if (quantity is < 1 or > 99) throw new InvalidQuantityPricingException("A priced line must contain 1–99 units.");
        var selected = tiers.Where(t => t.MinimumQuantity <= quantity).OrderByDescending(t => t.MinimumQuantity).FirstOrDefault();
        return new(basePrice, selected is null ? basePrice : new Money(Math.Min(basePrice.Amount, selected.UnitAmount), basePrice.Currency), selected?.MinimumQuantity);
    }
}
