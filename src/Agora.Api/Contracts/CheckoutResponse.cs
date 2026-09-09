using Agora.Infrastructure.Services;

namespace Agora.Api.Contracts;

/// <summary>Checkout-only receipt. Ordinary order reads never use this type.</summary>
public sealed record CheckoutResponse(
    string Number, string Status, string Email, AddressDto ShippingAddress, string Currency,
    decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal ShippingAmount, decimal Total,
    string? DiscountCode, string? GiftCardCode, decimal GiftCardAmount, string? PaymentTransactionId,
    string? ShippingMethodCode, string? ShippingMethodName, DateTimeOffset? EstimatedDeliveryFrom,
    DateTimeOffset? EstimatedDeliveryTo, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt,
    DateTimeOffset? FulfilledAt, DateTimeOffset? CancelledAt, DateTimeOffset? RefundedAt,
    IReadOnlyList<OrderItemResponse> Items, string? GuestOrderAccessToken,
    DateTimeOffset? GuestOrderAccessExpiresAt)
{
    public static CheckoutResponse From(CheckoutResult result)
    {
        var order = OrderResponse.From(result.Order);
        return new(order.Number, order.Status, order.Email, order.ShippingAddress, order.Currency,
            order.Subtotal, order.DiscountAmount, order.TaxAmount, order.ShippingAmount, order.Total,
            order.DiscountCode, null, order.GiftCardAmount, null,
            order.ShippingMethodCode, order.ShippingMethodName, order.EstimatedDeliveryFrom,
            order.EstimatedDeliveryTo, order.CreatedAt, order.PaidAt, order.FulfilledAt,
            order.CancelledAt, order.RefundedAt, order.Items, result.GuestOrderAccessToken,
            result.GuestOrderAccessExpiresAt);
    }
}
