using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum GiftCardEntryKind { OpeningBalance = 0, Issued = 1, Redeemed = 2, RefundCredit = 3 }

public class GiftCardEntry
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid GiftCardId { get; private set; }
    public int RecordedVersion { get; private set; }
    public GiftCardEntryKind Kind { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = Money.DefaultCurrency;
    public decimal BalanceAfter { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid? SourceOrderId { get; private set; }
    public Guid? SourceReturnId { get; private set; }
    private GiftCardEntry() { }

    public GiftCardEntry(GiftCard card, GiftCardEntryKind kind, decimal amount, DateTimeOffset now,
        Guid? sourceOrderId = null, Guid? sourceReturnId = null)
    {
        var validKind = kind switch
        {
            GiftCardEntryKind.OpeningBalance => amount >= 0 && amount == card.Balance && sourceOrderId is null && sourceReturnId is null,
            GiftCardEntryKind.Issued => amount > 0 && amount == card.Balance && card.Version == 0 && sourceOrderId is null && sourceReturnId is null,
            GiftCardEntryKind.Redeemed => amount < 0 && sourceOrderId is not null && sourceReturnId is null,
            GiftCardEntryKind.RefundCredit => amount > 0 && sourceOrderId is not null,
            _ => false
        };
        if (!validKind || decimal.Round(amount, 2) != amount || card.Balance < 0 || card.Version < 0
            || sourceOrderId == Guid.Empty || sourceReturnId == Guid.Empty)
            throw new DomainException("Gift-card entry kind, sign, precision, source, and balance must agree.");
        GiftCardId = card.Id; RecordedVersion = card.Version; Kind = kind; Amount = amount;
        Currency = card.Currency; BalanceAfter = card.Balance; RecordedAt = now;
        SourceOrderId = sourceOrderId; SourceReturnId = sourceReturnId;
    }
}
