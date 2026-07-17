using Agora.Domain.Common;
using Agora.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Agora.Tests.Unit;

public class CalculatorTests
{
    private static readonly IOptions<CheckoutOptions> Options =
        Microsoft.Extensions.Options.Options.Create(new CheckoutOptions
        {
            TaxRate = 0.08m,
            ShippingFlatRate = 5.99m,
            FreeShippingThreshold = 50m,
        });

    [Fact]
    public void Tax_IsRateTimesAmount_Rounded()
    {
        var tax = new FlatRateTaxCalculator(Options).CalculateTax(new Money(35.98m));

        Assert.Equal(2.88m, tax.Amount); // 2.8784 rounds up
    }

    [Fact]
    public void Tax_OnZero_IsZero()
    {
        var tax = new FlatRateTaxCalculator(Options).CalculateTax(Money.Zero());

        Assert.Equal(0m, tax.Amount);
    }

    [Fact]
    public void Shipping_UnderThreshold_IsFlatRate()
    {
        var shipping = new FlatRateShippingCalculator(Options).CalculateShipping(new Money(49.99m), 2);

        Assert.Equal(5.99m, shipping.Amount);
    }

    [Fact]
    public void Shipping_AtThreshold_IsFree()
    {
        var shipping = new FlatRateShippingCalculator(Options).CalculateShipping(new Money(50.00m), 2);

        Assert.Equal(0m, shipping.Amount);
    }

    [Fact]
    public void Shipping_PreservesCurrency()
    {
        var shipping = new FlatRateShippingCalculator(Options).CalculateShipping(new Money(10m, "EUR"), 1);

        Assert.Equal("EUR", shipping.Currency);
    }
}
