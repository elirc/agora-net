using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum ShippingRateType
{
    /// <summary>A single flat charge per shipment.</summary>
    Flat = 0,

    /// <summary>Base charge plus a per-kilogram rate on the cart's total weight.</summary>
    Weighted = 1,
}

/// <summary>
/// A selectable shipping option with its own rate rule and delivery estimate.
/// Orders at or above <see cref="FreeThreshold"/> (discounted subtotal) ship free.
/// </summary>
public class ShippingMethod
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable machine identifier used at checkout, e.g. "standard".</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public ShippingRateType RateType { get; set; } = ShippingRateType.Flat;
    public decimal BaseRate { get; set; }

    /// <summary>Per-kilogram charge; only applies to <see cref="ShippingRateType.Weighted"/>.</summary>
    public decimal PerKgRate { get; set; }

    /// <summary>Discounted subtotals at or above this amount ship free; null disables.</summary>
    public decimal? FreeThreshold { get; set; }

    public int MinDays { get; set; }
    public int MaxDays { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Used when checkout does not specify a method.</summary>
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Money CalculateCharge(Money discountedSubtotal, long totalWeightGrams)
    {
        if (totalWeightGrams < 0)
        {
            throw new DomainException("Total weight cannot be negative.");
        }

        if (FreeThreshold is { } threshold && discountedSubtotal.Amount >= threshold)
        {
            return Money.Zero(discountedSubtotal.Currency);
        }

        var charge = new Money(BaseRate, discountedSubtotal.Currency);
        if (RateType == ShippingRateType.Weighted)
        {
            charge = charge.Add(new Money(
                PerKgRate * (totalWeightGrams / 1000m), discountedSubtotal.Currency));
        }

        return charge;
    }
}
