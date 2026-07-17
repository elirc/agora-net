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
