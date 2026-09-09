using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record GuestCredentialResponse(Guid CredentialId, string GuestOrderAccessToken,
    DateTimeOffset ExpiresAt);

public sealed record CustomerOrderResponse(string Number, string Status, AddressDto ShippingAddress,
    string Currency, decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal ShippingAmount,
    decimal Total, decimal GiftCardAmount, string? ShippingMethodCode, string? ShippingMethodName,
    DateTimeOffset? EstimatedDeliveryFrom, DateTimeOffset? EstimatedDeliveryTo, DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt, DateTimeOffset? FulfilledAt, DateTimeOffset? CancelledAt,
    DateTimeOffset? RefundedAt, IReadOnlyList<OrderItemResponse> Items)
{
    public static CustomerOrderResponse From(Order order) => new(order.Number, order.Status.ToString(),
        AddressDto.From(order.ShippingAddress), order.Currency, order.Subtotal, order.DiscountAmount,
        order.TaxAmount, order.ShippingAmount, order.Total, order.GiftCardAmount,
        order.ShippingMethodCode, order.ShippingMethodName, order.EstimatedDeliveryFrom,
        order.EstimatedDeliveryTo, order.CreatedAt, order.PaidAt, order.FulfilledAt, order.CancelledAt,
        order.RefundedAt, order.Items.Select(OrderItemResponse.From).ToArray());
}

public sealed record CustomerReturnResponse(string Number, string OrderNumber, string Status, string Reason,
    string Comment, string? RejectionNote, decimal RefundAmount, string Currency, DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt, IReadOnlyList<ReturnItemResponse> Items)
{
    public static CustomerReturnResponse From(ReturnRequest request) => new(request.Number,
        request.Order?.Number ?? string.Empty, request.Status.ToString(), request.Reason.ToString(),
        request.Comment, request.RejectionNote, request.RefundAmount, request.Currency, request.CreatedAt,
        request.ProcessedAt, request.Items.Select(ReturnItemResponse.From).ToArray());
}
