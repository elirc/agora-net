using Agora.Domain.Entities;

namespace Agora.Domain.Services;

public sealed record ReturnPolicyDecision(DateTimeOffset? Deadline, IReadOnlyList<string> Reasons);

public static class ReturnEligibilityRules
{
    public static ReturnPolicyDecision Evaluate(OrderStatus status, DateTimeOffset? fulfilledAt, int? windowDays, DateTimeOffset now)
    {
        if (windowDays is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(windowDays));
        var reasons = new List<string>(); DateTimeOffset? deadline = null;
        if (status != OrderStatus.Fulfilled) reasons.Add("OrderNotFulfilled");
        if (windowDays is { } days)
        {
            if (fulfilledAt is null) reasons.Add("MissingFulfilledAt");
            else
            {
                try { deadline = fulfilledAt.Value.AddDays(days); }
                catch (ArgumentOutOfRangeException) { reasons.Add("InvalidFulfilledAt"); }
                if (deadline is not null && now >= deadline) reasons.Add("ReturnWindowExpired");
            }
        }
        return new(deadline, reasons);
    }

    // Preserve the existing order-effective discount/tax allocation for partial returns.
    public static decimal EstimateRefund(Order order, OrderItem item, int quantity)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        var discountRate = order.Subtotal > 0 ? order.DiscountAmount / order.Subtotal : 0m;
        var discountedSubtotal = order.Subtotal - order.DiscountAmount;
        var taxRate = discountedSubtotal > 0 ? order.TaxAmount / discountedSubtotal : 0m;
        return decimal.Round(item.UnitPrice * quantity * (1 - discountRate) * (1 + taxRate), 2, MidpointRounding.AwayFromZero);
    }
}
