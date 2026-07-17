using Agora.Domain.Common;
using Agora.Domain.Services;

namespace Agora.Infrastructure.Services;

public sealed class CheckoutOptions
{
    public const string SectionName = "Checkout";

    /// <summary>Flat tax rate applied to the discounted subtotal.</summary>
    public decimal TaxRate { get; set; } = 0.08m;

    /// <summary>Flat shipping charge.</summary>
    public decimal ShippingFlatRate { get; set; } = 5.99m;

    /// <summary>Discounted subtotals at or above this ship free.</summary>
    public decimal FreeShippingThreshold { get; set; } = 50m;
}

public sealed class FlatRateTaxCalculator(Microsoft.Extensions.Options.IOptions<CheckoutOptions> options)
    : ITaxCalculator
{
    public Money CalculateTax(Money taxableAmount) =>
        taxableAmount.Multiply(options.Value.TaxRate);
}

public sealed class FlatRateShippingCalculator(Microsoft.Extensions.Options.IOptions<CheckoutOptions> options)
    : IShippingCalculator
{
    public Money CalculateShipping(Money discountedSubtotal, int totalQuantity)
    {
        if (totalQuantity <= 0 || discountedSubtotal.Amount >= options.Value.FreeShippingThreshold)
        {
            return Money.Zero(discountedSubtotal.Currency);
        }

        return new Money(options.Value.ShippingFlatRate, discountedSubtotal.Currency);
    }
}
