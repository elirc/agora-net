using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum OrderStatus
{
    Pending = 0,
    Paid = 1,
    Fulfilled = 2,
    Cancelled = 3,
    Refunded = 4,
}

/// <summary>
/// Customer order. Lifecycle: Pending -> Paid -> Fulfilled, with Cancelled
/// reachable from Pending/Paid and Refunded reachable from Paid/Fulfilled.
/// </summary>
public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Owning account for signed-in checkouts; null for guest orders.</summary>
    public Guid? CustomerId { get; set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public Address ShippingAddress { get; set; } = new();

    public string Currency { get; set; } = Money.DefaultCurrency;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal Total { get; set; }

    public string? DiscountCode { get; set; }
    public string? PaymentTransactionId { get; private set; }

    public string? ShippingMethodCode { get; set; }
    public string? ShippingMethodName { get; set; }
    public DateTimeOffset? EstimatedDeliveryFrom { get; set; }
    public DateTimeOffset? EstimatedDeliveryTo { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }

    public List<OrderItem> Items { get; set; } = [];

    public void MarkPaid(string paymentTransactionId, DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOrderStateException(
                $"Order {Number} cannot be marked paid from status {Status}.");
        }

        PaymentTransactionId = paymentTransactionId;
        Status = OrderStatus.Paid;
        PaidAt = now;
    }

    public void MarkFulfilled(DateTimeOffset now)
    {
        if (Status != OrderStatus.Paid)
        {
            throw new InvalidOrderStateException(
                $"Order {Number} cannot be fulfilled from status {Status}.");
        }

        Status = OrderStatus.Fulfilled;
        FulfilledAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.Paid))
        {
            throw new InvalidOrderStateException(
                $"Order {Number} cannot be cancelled from status {Status}.");
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = now;
    }

    public void Refund(DateTimeOffset now)
    {
        if (Status is not (OrderStatus.Paid or OrderStatus.Fulfilled))
        {
            throw new InvalidOrderStateException(
                $"Order {Number} cannot be refunded from status {Status}.");
        }

        Status = OrderStatus.Refunded;
        RefundedAt = now;
    }
}
