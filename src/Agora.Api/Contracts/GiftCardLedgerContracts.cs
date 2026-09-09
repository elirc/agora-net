using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record GiftCardEntryResponse(Guid Id, Guid GiftCardId, int RecordedVersion, string Kind, decimal Amount,
    string Currency, decimal BalanceAfter, DateTimeOffset RecordedAt, Guid? SourceOrderId, Guid? SourceReturnId)
{
    public static GiftCardEntryResponse From(GiftCardEntry e) => new(e.Id, e.GiftCardId, e.RecordedVersion, e.Kind.ToString(),
        e.Amount, e.Currency, e.BalanceAfter, e.RecordedAt, e.SourceOrderId, e.SourceReturnId);
}
public sealed record GiftCardLedgerResponse(Guid GiftCardId, string Currency, string? HistoryStartsWith,
    int? OpeningRecordedVersion, PagedResult<GiftCardEntryResponse> Entries);
