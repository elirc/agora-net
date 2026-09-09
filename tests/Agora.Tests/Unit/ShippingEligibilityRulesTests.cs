using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class ShippingEligibilityRulesTests
{
    [Theory]
    [InlineData("US", 2000, true)]
    [InlineData("us", 2001, false)]
    [InlineData("GB", 500, false)]
    public void Country_and_weight_boundaries_are_independent(string country, long weight, bool eligible)
    {
        var result = ShippingEligibilityRules.Evaluate(["CA", "US"], 2000, country, weight);
        Assert.Equal(eligible, result.Eligible);
        Assert.Equal(country.Equals("GB", StringComparison.OrdinalIgnoreCase), result.Reasons.Contains("CountryNotServed"));
        Assert.Equal(weight > 2000, result.Reasons.Contains("WeightExceeded"));
    }
    [Fact]
    public void Empty_country_list_and_null_cap_mean_unrestricted()
        => Assert.True(ShippingEligibilityRules.Evaluate([], null, "nz", long.MaxValue).Eligible);
    [Fact]
    public void Negative_legacy_weight_is_rejected_before_policy_evaluation()
        => Assert.Throws<DomainException>(() => ShippingEligibilityRules.Evaluate([], null, "US", -1));
    [Theory]
    [InlineData("")]
    [InlineData("USA")]
    [InlineData("U1")]
    [InlineData("\u212AK")]
    public void Country_is_syntactic_ascii_not_address_verification(string country)
        => Assert.Throws<DomainException>(() => ShippingEligibilityRules.Evaluate([], null, country, 0));
}
