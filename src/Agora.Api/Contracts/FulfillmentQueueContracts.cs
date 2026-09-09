namespace Agora.Api.Contracts;

public sealed record FulfillmentQueueLineResponse(Guid OrderItemId, string Sku, string ProductName,
    string VariantName, int OrderedQuantity, long FulfilledQuantity, long RemainingQuantity);
public sealed record FulfillmentQueueOrderResponse(string Number, DateTimeOffset? PaidAt,
    string? ShippingMethodCode, string? ShippingMethodName, bool IsHeld,
    IReadOnlyList<FulfillmentQueueLineResponse> Lines);
