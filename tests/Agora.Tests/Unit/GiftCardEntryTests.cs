using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class GiftCardEntryTests
{
    [Fact]
    public void Kinds_require_meaningful_signs_sources_and_whole_cents()
    {
        var card = new GiftCard(50); var now = DateTimeOffset.UnixEpoch; var order = Guid.NewGuid();
        Assert.Equal(50m, new GiftCardEntry(card, GiftCardEntryKind.Issued, 50, now).Amount);
        Assert.Throws<DomainException>(() => new GiftCardEntry(card, GiftCardEntryKind.Issued, 49, now));
        Assert.Throws<DomainException>(() => new GiftCardEntry(card, GiftCardEntryKind.Redeemed, 10, now, order));
        Assert.Throws<DomainException>(() => new GiftCardEntry(card, GiftCardEntryKind.Redeemed, -10, now));
        Assert.Throws<DomainException>(() => new GiftCardEntry(card, GiftCardEntryKind.RefundCredit, -5, now, order));
        Assert.Throws<DomainException>(() => new GiftCardEntry(card, GiftCardEntryKind.RefundCredit, 5.001m, now, order));
        Assert.Throws<DomainException>(() => new GiftCardEntry(card, (GiftCardEntryKind)999, 50, now));
        card.Redeem(50); Assert.Equal(0m, new GiftCardEntry(card, GiftCardEntryKind.OpeningBalance, 0, now).Amount);
        Assert.Throws<DomainException>(() => new GiftCardEntry(card, GiftCardEntryKind.Issued, 0, now));
    }
}
