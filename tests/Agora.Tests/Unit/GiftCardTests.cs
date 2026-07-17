using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class GiftCardTests
{
    [Fact]
    public void NewCard_HasFullBalance_AndGeneratedCode()
    {
        var card = new GiftCard(50m);

        Assert.Equal(50m, card.Balance);
        Assert.Equal(50m, card.InitialBalance);
        Assert.StartsWith("GC-", card.Code);
        Assert.True(card.IsRedeemable(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void NonPositiveAmount_Throws()
    {
        Assert.Throws<DomainException>(() => new GiftCard(0m));
        Assert.Throws<DomainException>(() => new GiftCard(-5m));
    }

    [Fact]
    public void Redeem_DecrementsBalance()
    {
        var card = new GiftCard(50m);

        card.Redeem(20m);

        Assert.Equal(30m, card.Balance);
    }

    [Fact]
    public void Redeem_BeyondBalance_Throws()
    {
        var card = new GiftCard(10m);

        Assert.Throws<InvalidGiftCardException>(() => card.Redeem(10.01m));
    }

    [Fact]
    public void DrainedCard_IsNotRedeemable()
    {
        var card = new GiftCard(10m);
        card.Redeem(10m);

        Assert.False(card.IsRedeemable(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ExpiredCard_IsNotRedeemable()
    {
        var card = new GiftCard(10m, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        Assert.False(card.IsRedeemable(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void InactiveCard_IsNotRedeemable()
    {
        var card = new GiftCard(10m) { IsActive = false };

        Assert.False(card.IsRedeemable(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Credit_RestoresBalance()
    {
        var card = new GiftCard(50m);
        card.Redeem(50m);

        card.Credit(50m);

        Assert.Equal(50m, card.Balance);
        Assert.True(card.IsRedeemable(DateTimeOffset.UtcNow));
    }
}
