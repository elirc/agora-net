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
}
