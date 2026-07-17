using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

/// <summary>Sprint 13 additions to the order lifecycle around shipment states.</summary>
public class OrderFulfillmentStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static Order PaidOrder()
    {
        var order = new Order { Number = "ORD-TEST-0002", Email = "a@b.com" };
        order.MarkPaid("txn", Now);
        return order;
    }

    [Fact]
    public void MarkPartiallyFulfilled_FromPaid_Succeeds()
    {
        var order = PaidOrder();

        order.MarkPartiallyFulfilled();

        Assert.Equal(OrderStatus.PartiallyFulfilled, order.Status);
        Assert.Null(order.FulfilledAt);
    }

    [Fact]
    public void MarkFulfilled_FromPartiallyFulfilled_Succeeds()
    {
        var order = PaidOrder();
        order.MarkPartiallyFulfilled();

        order.MarkFulfilled(Now);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(Now, order.FulfilledAt);
    }

    [Fact]
    public void MarkPartiallyFulfilled_FromPending_Throws()
    {
        var order = new Order { Number = "ORD-X", Email = "a@b.com" };

        Assert.Throws<InvalidOrderStateException>(order.MarkPartiallyFulfilled);
    }

    [Fact]
    public void Cancel_FromPartiallyFulfilled_Throws()
    {
        var order = PaidOrder();
        order.MarkPartiallyFulfilled();

        Assert.Throws<InvalidOrderStateException>(() => order.Cancel(Now));
    }

    [Fact]
    public void Refund_FromPartiallyFulfilled_Succeeds()
    {
        var order = PaidOrder();
        order.MarkPartiallyFulfilled();

        order.Refund(Now);

        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    [Fact]
    public void MarkPartiallyFulfilled_AfterFullyFulfilled_Throws()
    {
        var order = PaidOrder();
        order.MarkFulfilled(Now);

        Assert.Throws<InvalidOrderStateException>(order.MarkPartiallyFulfilled);
    }
}
