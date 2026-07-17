using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record AddressDto(
    [Required, MaxLength(200)] string FullName,
    [Required, MaxLength(200)] string Line1,
    [MaxLength(200)] string? Line2,
    [Required, MaxLength(100)] string City,
    [Required, MaxLength(100)] string Region,
    [Required, MaxLength(20)] string PostalCode,
    [Required, MaxLength(2)] string Country)
{
    public Address ToAddress() => new()
    {
        FullName = FullName,
        Line1 = Line1,
        Line2 = Line2,
        City = City,
        Region = Region,
        PostalCode = PostalCode,
        Country = Country,
    };

    public static AddressDto From(Address address) => new(
        address.FullName,
        address.Line1,
        address.Line2,
        address.City,
        address.Region,
        address.PostalCode,
        address.Country);
}

public sealed record CheckoutRequest(
    [Required] string CartToken,
    [Required, EmailAddress, MaxLength(320)] string Email,
    AddressDto? ShippingAddress,
    [MaxLength(64)] string? DiscountCode,
    [Required, MaxLength(128)] string PaymentToken,
    [MaxLength(64)] string? ShippingMethodCode = null,
    Guid? ShippingAddressId = null);

public sealed record OrderItemResponse(
    Guid Id,
    Guid ProductVariantId,
    string Sku,
    string ProductName,
    string VariantName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal)
{
    public static OrderItemResponse From(OrderItem item) => new(
        item.Id,
        item.ProductVariantId,
        item.Sku,
        item.ProductName,
        item.VariantName,
        item.UnitPrice,
        item.Quantity,
        item.LineTotal);
}

public sealed record OrderResponse(
    string Number,
    string Status,
    string Email,
    AddressDto ShippingAddress,
    string Currency,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal ShippingAmount,
    decimal Total,
    string? DiscountCode,
    string? PaymentTransactionId,
    string? ShippingMethodCode,
    string? ShippingMethodName,
    DateTimeOffset? EstimatedDeliveryFrom,
    DateTimeOffset? EstimatedDeliveryTo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? FulfilledAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? RefundedAt,
    IReadOnlyList<OrderItemResponse> Items)
{
    public static OrderResponse From(Order order) => new(
        order.Number,
        order.Status.ToString(),
        order.Email,
        AddressDto.From(order.ShippingAddress),
        order.Currency,
        order.Subtotal,
        order.DiscountAmount,
        order.TaxAmount,
        order.ShippingAmount,
        order.Total,
        order.DiscountCode,
        order.PaymentTransactionId,
        order.ShippingMethodCode,
        order.ShippingMethodName,
        order.EstimatedDeliveryFrom,
        order.EstimatedDeliveryTo,
        order.CreatedAt,
        order.PaidAt,
        order.FulfilledAt,
        order.CancelledAt,
        order.RefundedAt,
        order.Items.Select(OrderItemResponse.From).ToList());
}
