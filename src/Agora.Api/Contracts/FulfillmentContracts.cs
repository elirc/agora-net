using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record FulfillmentLineDto(
    [Required] Guid OrderItemId,
    [Range(1, 999)] int Quantity);

public sealed record CreateFulfillmentRequest(
    [MaxLength(100)] string? Carrier,
    [MaxLength(100)] string? TrackingNumber,
    List<FulfillmentLineDto>? Items);

public sealed record FulfillmentItemResponse(
    Guid OrderItemId,
    Guid ProductVariantId,
    string Sku,
    int Quantity)
{
    public static FulfillmentItemResponse From(FulfillmentItem item) => new(
        item.OrderItemId,
        item.ProductVariantId,
        item.Sku,
        item.Quantity);
}

public sealed record FulfillmentResponse(
    string Number,
    string Carrier,
    string? TrackingNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<FulfillmentItemResponse> Items)
{
    public static FulfillmentResponse From(Fulfillment fulfillment) => new(
        fulfillment.Number,
        fulfillment.Carrier,
        fulfillment.TrackingNumber,
        fulfillment.CreatedAt,
        fulfillment.Items.Select(FulfillmentItemResponse.From).ToList());
}
