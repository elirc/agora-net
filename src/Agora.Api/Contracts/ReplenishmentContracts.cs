namespace Agora.Api.Contracts;

public sealed record ReplenishmentRow(Guid VariantId, string Sku, string ProductName, string VariantName,
    long NetUnits, decimal DailyAverage, long AvailableUnits, long SuggestedUnits);
public sealed record ReplenishmentReportResponse(DateTimeOffset AsOf, DateTimeOffset From, DateTimeOffset To,
    int WindowDays, int CoverDays, PagedResult<ReplenishmentRow> Variants);
