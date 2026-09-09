using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum ShipmentTrackingStatus { Unknown = 0, InTransit = 1, OutForDelivery = 2, Delivered = 3, Exception = 4 }

public class ShipmentTrackingEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FulfillmentId { get; private set; }
    public long Sequence { get; private set; }
    public ShipmentTrackingStatus Status { get; private set; }
    public string? Message { get; private set; }
    public Guid ActorAdminId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    private ShipmentTrackingEvent() { }
    internal ShipmentTrackingEvent(Guid fulfillmentId, long sequence, ShipmentTrackingStatus status, string? message, Guid actor, DateTimeOffset now)
    { FulfillmentId = fulfillmentId; Sequence = sequence; Status = status; Message = message; ActorAdminId = actor; RecordedAt = now; }
}

public sealed class InvalidShipmentTrackingTransitionException(string message) : DomainException(message);
