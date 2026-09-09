namespace Agora.Api.Contracts;

public sealed record OrderTimelineEntry(string Key, string Type, DateTimeOffset RecordedAt,
    Guid RelatedId, string RelatedNumber, string Label);
