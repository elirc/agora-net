using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;

namespace Agora.Infrastructure.Services;

/// <summary>Stages each balance mutation and its ledger entry in the caller's existing unit of work. Never saves or calls providers.</summary>
public static class GiftCardAccounting
{
    public static void Issue(AgoraDbContext db, GiftCard card, DateTimeOffset now)
    {
        var entry = new GiftCardEntry(card, GiftCardEntryKind.Issued, card.Balance, now);
        card.CreatedAt = now; db.GiftCards.Add(card); db.GiftCardEntries.Add(entry);
    }
    public static void Redeem(AgoraDbContext db, GiftCard card, decimal amount, Guid orderId, DateTimeOffset now)
    {
        ValidateMutation(amount, orderId, null);
        card.Redeem(amount);
        db.GiftCardEntries.Add(new GiftCardEntry(card, GiftCardEntryKind.Redeemed, -amount, now, orderId));
    }
    public static void Credit(AgoraDbContext db, GiftCard card, decimal amount, Guid orderId, Guid? returnId, DateTimeOffset now)
    {
        ValidateMutation(amount, orderId, returnId);
        card.Credit(amount);
        db.GiftCardEntries.Add(new GiftCardEntry(card, GiftCardEntryKind.RefundCredit, amount, now, orderId, returnId));
    }
    private static void ValidateMutation(decimal amount, Guid orderId, Guid? returnId)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount || orderId == Guid.Empty || returnId == Guid.Empty)
            throw new DomainException("A card mutation requires positive whole cents and a valid source identity.");
    }
}
