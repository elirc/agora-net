using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

/// <summary>
/// Exact-instant and exact-amount boundaries for discount codes and gift
/// cards: expiry is exclusive at the stored instant, usage limits and balances
/// flip at precisely their last unit/cent.
/// </summary>
public class RedeemabilityBoundaryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void DiscountCode_AtExactExpiryInstant_IsNotRedeemable()
    {
        var code = new DiscountCode
        {
            Code = "EDGE",
            Type = DiscountType.Percentage,
            Value = 10m,
            ExpiresAt = Now,
        };

        Assert.False(code.IsRedeemable(Now));
        Assert.True(code.IsRedeemable(Now.AddTicks(-1)));
    }

    [Fact]
    public void DiscountCode_UsageLimit_FlipsAtExactlyTheLimit()
    {
        var code = new DiscountCode
        {
            Code = "LIMITED",
            Type = DiscountType.FixedAmount,
            Value = 5m,
            UsageLimit = 3,
            TimesUsed = 2,
        };

        Assert.True(code.IsRedeemable(Now)); // one use left

        code.RegisterUse(Now);

        Assert.Equal(3, code.TimesUsed);
        Assert.False(code.IsRedeemable(Now)); // limit reached exactly
    }

    [Fact]
    public void GiftCard_AtExactExpiryInstant_IsNotRedeemable()
    {
        var card = new GiftCard(25m, expiresAt: Now);

        Assert.False(card.IsRedeemable(Now));
        Assert.True(card.IsRedeemable(Now.AddTicks(-1)));
    }

    [Fact]
    public void GiftCard_RedeemingExactBalance_LeavesZero_AndKillsRedeemability()
    {
        var card = new GiftCard(25m);

        card.Redeem(25m);

        Assert.Equal(0m, card.Balance);
        Assert.False(card.IsRedeemable(Now));
    }

    [Fact]
    public void GiftCard_RedeemingOneCentOverBalance_Throws()
    {
        var card = new GiftCard(25m);

        Assert.Throws<Agora.Domain.Common.InvalidGiftCardException>(() => card.Redeem(25.01m));
        Assert.Equal(25m, card.Balance); // untouched by the failed draw
    }

    [Fact]
    public void GiftCard_BalanceMutations_BumpTheConcurrencyVersion()
    {
        var card = new GiftCard(25m);
        var initial = card.Version;

        card.Redeem(10m);
        card.Credit(5m);

        Assert.Equal(initial + 2, card.Version);
    }
}
