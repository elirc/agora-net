using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Agora.Api.Contracts;

public sealed record PutDeliveryCalendarRequest(bool Enabled,
    [Required, RegularExpression("^(?:[01][0-9]|2[0-3]):[0-5][0-9]$")] string CutoffUtc,
    [Required, MaxLength(366)] List<DateOnly> ClosureDates,
    [property: JsonRequired] [Range(0, long.MaxValue)] long? ExpectedRevision);
public sealed record DeliveryCalendarResponse(bool Enabled, string CutoffUtc, IReadOnlyList<DateOnly> ClosureDates, long Revision);
