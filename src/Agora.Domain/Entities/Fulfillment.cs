using Agora.Domain.Common;

namespace Agora.Domain.Entities;

/// <summary>
/// A shipment covering some or all of an order's lines. Orders derive their
/// status from their fulfillments: any shipment makes them PartiallyFulfilled
/// and full line coverage makes them Fulfilled.
/// </summary>
public class Fulfillment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }

    public string Carrier { get; set; } = string.Empty;
    public string? TrackingNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<FulfillmentItem> Items { get; set; } = [];
    public ShipmentTrackingStatus TrackingStatus { get; private set; } = ShipmentTrackingStatus.Unknown;
    public long TrackingVersion { get; private set; }

    public ShipmentTrackingEvent RecordTracking(ShipmentTrackingStatus next, string? message, Guid actor, DateTimeOffset now)
    {
        var allowed = TrackingStatus switch
        {
            ShipmentTrackingStatus.Unknown => next is ShipmentTrackingStatus.InTransit or ShipmentTrackingStatus.Exception,
            ShipmentTrackingStatus.InTransit => next is ShipmentTrackingStatus.OutForDelivery or ShipmentTrackingStatus.Delivered or ShipmentTrackingStatus.Exception,
            ShipmentTrackingStatus.OutForDelivery => next is ShipmentTrackingStatus.Delivered or ShipmentTrackingStatus.Exception,
            ShipmentTrackingStatus.Exception => next is ShipmentTrackingStatus.InTransit or ShipmentTrackingStatus.OutForDelivery or ShipmentTrackingStatus.Delivered,
            _ => false
        };
        if (!allowed) throw new InvalidShipmentTrackingTransitionException($"Cannot move shipment tracking from {TrackingStatus} to {next}.");
        var text = message?.Trim();
        if (text?.Length > 200) throw new DomainException("Tracking message must contain at most 200 characters.");
        var version = checked(TrackingVersion + 1);
        var record = new ShipmentTrackingEvent(Id, version, next, string.IsNullOrEmpty(text) ? null : text, actor, now);
        TrackingStatus = next; TrackingVersion = version;
        return record;
    }
}

/// <summary>Quantity of one order line included in a shipment.</summary>
public class FulfillmentItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FulfillmentId { get; set; }
    public Fulfillment? Fulfillment { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid ProductVariantId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
