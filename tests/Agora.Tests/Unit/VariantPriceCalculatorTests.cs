using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;

namespace Agora.Tests.Unit;

public class VariantPriceCalculatorTests
{
    [Theory]
    [InlineData(1, 10, null)]
    [InlineData(4, 40, null)]
    [InlineData(5, 45, 5)]
    [InlineData(9, 81, 5)]
    [InlineData(10, 80, 10)]
    [InlineData(99, 792, 10)]
    public void Highest_qualifying_threshold_applies_to_one_line(int quantity, decimal total, int? threshold)
    {
        var policy = new VariantQuantityPricing(Guid.NewGuid(), [new(5, 9), new(10, 8)], 10);
        var result = VariantPriceCalculator.Calculate(new Money(10), quantity, policy.Tiers);
        Assert.Equal(total, result.AppliedPrice.Multiply(quantity).Amount); Assert.Equal(threshold, result.MinimumQuantity);
        Assert.Equal(10, result.BasePrice.Amount);
    }
    [Fact]
    public void Reduced_base_never_becomes_surcharge_and_zero_tier_and_empty_policy_are_supported()
    {
        var policy = new VariantQuantityPricing(Guid.NewGuid(), [new(5, 9), new(10, 8)], 10);
        Assert.Equal(7, VariantPriceCalculator.Calculate(new Money(7, "EUR"), 10, policy.Tiers).AppliedPrice.Amount);
        policy.Replace([new(2, 0)], 7);
        Assert.Equal(0, VariantPriceCalculator.Calculate(new Money(7), 2, policy.Tiers).AppliedPrice.Amount);
        policy.Replace([], 7); Assert.Equal(7, VariantPriceCalculator.Calculate(new Money(7), 99, policy.Tiers).AppliedPrice.Amount);
    }
    [Fact]
    public void Invalid_replacements_do_not_mutate_policy_or_revision()
    {
        var policy = new VariantQuantityPricing(Guid.NewGuid(), [new(5, 9)], 10);
        QuantityTierInput[][] invalid = [[new(1, 9)], [new(100, 9)], [new(5, 9), new(5, 8)], [new(10, 8), new(5, 7)],
            [new(5, 11)], [new(5, -1)], [new(5, 8.001m)], [new(5, 8), new(10, 9)],
            [new(2, 9), new(3, 8), new(4, 7), new(5, 6), new(6, 5), new(7, 4)]];
        foreach (var input in invalid)
        {
            Assert.Throws<InvalidQuantityPricingException>(() => policy.Replace(input, 10));
            Assert.Equal(0L, policy.Revision); Assert.Equal(9, Assert.Single(policy.Tiers).UnitAmount);
        }
    }
}
