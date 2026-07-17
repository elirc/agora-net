using Agora.Domain.Common;
using Agora.Domain.Services;

namespace Agora.Infrastructure.Services;

public sealed class CheckoutOptions
{
    public const string SectionName = "Checkout";

    /// <summary>Flat tax rate applied to the discounted subtotal.</summary>
    public decimal TaxRate { get; set; } = 0.08m;
}

public sealed class FlatRateTaxCalculator(Microsoft.Extensions.Options.IOptions<CheckoutOptions> options)
    : ITaxCalculator
{
    public Money CalculateTax(Money taxableAmount) =>
        taxableAmount.Multiply(options.Value.TaxRate);
}
