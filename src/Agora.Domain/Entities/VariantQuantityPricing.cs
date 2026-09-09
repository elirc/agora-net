using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public sealed record QuantityTierInput(int MinimumQuantity, decimal UnitAmount);
public sealed class InvalidQuantityPricingException(string message) : DomainException(message);

public sealed class VariantQuantityPricing
{
    public Guid ProductVariantId { get; private set; }
    public long Revision { get; private set; }
    public List<VariantQuantityTier> Tiers { get; private set; } = [];
    private VariantQuantityPricing() { }
    public VariantQuantityPricing(Guid variantId, IReadOnlyList<QuantityTierInput> tiers, decimal baseAmount)
    {
        ProductVariantId = variantId;
        Validate(tiers, baseAmount);
        Tiers = tiers.Select(t => new VariantQuantityTier(variantId, t.MinimumQuantity, t.UnitAmount)).ToList();
    }
    public void Replace(IReadOnlyList<QuantityTierInput> tiers, decimal baseAmount)
    {
        Validate(tiers, baseAmount);
        var nextRevision = checked(Revision + 1);
        var wanted = tiers.Select(t => t.MinimumQuantity).ToHashSet();
        Tiers.RemoveAll(t => !wanted.Contains(t.MinimumQuantity));
        foreach (var input in tiers)
        {
            var existing = Tiers.SingleOrDefault(t => t.MinimumQuantity == input.MinimumQuantity);
            if (existing is null) Tiers.Add(new VariantQuantityTier(ProductVariantId, input.MinimumQuantity, input.UnitAmount));
            else existing.SetAmount(input.UnitAmount);
        }
        Revision = nextRevision;
    }
    private static void Validate(IReadOnlyList<QuantityTierInput> tiers, decimal baseAmount)
    {
        if (tiers.Count > 5) throw new InvalidQuantityPricingException("At most five quantity tiers are allowed.");
        var previousQuantity = 1;
        var previousAmount = baseAmount;
        foreach (var tier in tiers)
        {
            if (tier is null || tier.MinimumQuantity is < 2 or > 99 || tier.MinimumQuantity <= previousQuantity)
                throw new InvalidQuantityPricingException("Tier thresholds must be distinct and increasing from 2 through 99.");
            if (tier.UnitAmount < 0 || decimal.Round(tier.UnitAmount, 2) != tier.UnitAmount || tier.UnitAmount > previousAmount)
                throw new InvalidQuantityPricingException("Tier amounts must be nonnegative whole cents, nonincreasing, and no greater than the current base price.");
            previousQuantity = tier.MinimumQuantity;
            previousAmount = tier.UnitAmount;
        }
    }
}

public sealed class VariantQuantityTier
{
    public Guid ProductVariantId { get; private set; }
    public int MinimumQuantity { get; private set; }
    public decimal UnitAmount { get; private set; }
    private VariantQuantityTier() { }
    internal VariantQuantityTier(Guid variantId, int minimumQuantity, decimal unitAmount)
    { ProductVariantId = variantId; MinimumQuantity = minimumQuantity; UnitAmount = unitAmount; }
    internal void SetAmount(decimal amount) => UnitAmount = amount;
}
