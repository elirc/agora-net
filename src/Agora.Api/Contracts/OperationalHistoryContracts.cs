using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddReturnEvidenceRequest([Required, MaxLength(2000)] string Url, [MaxLength(200)] string? Description = null);
public sealed record ReturnEvidenceResponse(Guid Id, string Url, string? Description, Guid AuthorCustomerId, DateTimeOffset CreatedAt)
{
    public static ReturnEvidenceResponse From(ReturnEvidence e) => new(e.Id, e.Url, e.Description, e.AuthorCustomerId, e.CreatedAt);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddOrderSupportNoteRequest([Required, MaxLength(1000)] string Body);
public sealed record OrderSupportNoteResponse(Guid Id, string Body, Guid AuthorAdminId, DateTimeOffset CreatedAt)
{
    public static OrderSupportNoteResponse From(OrderSupportNote n) => new(n.Id, n.Body, n.AuthorAdminId, n.CreatedAt);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddShipmentTrackingRequest([Required, Range(0, long.MaxValue)] long? ExpectedVersion,
    [Required, MaxLength(32)] string Status, [MaxLength(200)] string? Message = null);
public sealed record ShipmentTrackingEventResponse(Guid Id, long Sequence, string Status, string? Message, DateTimeOffset RecordedAt);
public sealed record AdminShipmentTrackingEventResponse(Guid Id, long Sequence, string Status, string? Message, DateTimeOffset RecordedAt, Guid ActorAdminId);
public sealed record ShipmentTrackingHistoryResponse(Guid FulfillmentId, string Status, long Version, PagedResult<ShipmentTrackingEventResponse> Events);
public sealed record AdminShipmentTrackingHistoryResponse(Guid FulfillmentId, string Status, long Version, PagedResult<AdminShipmentTrackingEventResponse> Events);
