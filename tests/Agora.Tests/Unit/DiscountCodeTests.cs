using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class DiscountCodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CalculateDiscount_Percentage_TakesPercentOfSubtotal()
    {
        var code = new DiscountCode { Code = "TEN", Type = DiscountType.Percentage, Value = 10m };

        var discount = code.CalculateDiscount(new Money(50m));

        Assert.Equal(5.00m, discount.Amount);
    }

    [Fact]
    public void CalculateDiscount_FixedAmount_UsesValue()
    {
        var code = new DiscountCode { Code = "FIVE", Type = DiscountType.FixedAmount, Value = 5m };

        var discount = code.CalculateDiscount(new Money(50m));

        Assert.Equal(5.00m, discount.Amount);
    }

    [Fact]
    public void CalculateDiscount_FixedAmount_ClampsToSubtotal()
    {
        var code = new DiscountCode { Code = "BIG", Type = DiscountType.FixedAmount, Value = 100m };

        var discount = code.CalculateDiscount(new Money(20m));

        Assert.Equal(20m, discount.Amount);
    }

    [Fact]
    public void IsRedeemable_ActiveUnexpiredUnderLimit_True()
    {
        var code = new DiscountCode
        {
            Code = "OK",
            Type = DiscountType.Percentage,
            Value = 10m,
            ExpiresAt = Now.AddDays(1),
            UsageLimit = 5,
            TimesUsed = 4,
        };

        Assert.True(code.IsRedeemable(Now));
    }

    [Fact]
    public void IsRedeemable_Expired_False()
    {
        var code = new DiscountCode { Code = "OLD", ExpiresAt = Now.AddMinutes(-1) };

        Assert.False(code.IsRedeemable(Now));
    }

    [Fact]
    public void IsRedeemable_UsageLimitReached_False()
    {
        var code = new DiscountCode { Code = "MAXED", UsageLimit = 3, TimesUsed = 3 };

        Assert.False(code.IsRedeemable(Now));
    }

    [Fact]
    public void IsRedeemable_Inactive_False()
    {
        var code = new DiscountCode { Code = "OFF", IsActive = false };

        Assert.False(code.IsRedeemable(Now));
    }

    [Fact]
    public void RegisterUse_IncrementsTimesUsed()
    {
        var code = new DiscountCode { Code = "USE", UsageLimit = 2 };

        code.RegisterUse(Now);

        Assert.Equal(1, code.TimesUsed);
    }

    [Fact]
    public void RegisterUse_WhenNotRedeemable_Throws()
    {
        var code = new DiscountCode { Code = "DEAD", IsActive = false };

        Assert.Throws<InvalidDiscountException>(() => code.RegisterUse(Now));
    }
}
