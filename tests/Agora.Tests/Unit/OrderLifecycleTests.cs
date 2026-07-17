using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class OrderLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    private static Order NewOrder() => new() { Number = "ORD-TEST-0001", Email = "a@b.com" };

    [Fact]
    public void NewOrder_StartsPending()
    {
        Assert.Equal(OrderStatus.Pending, NewOrder().Status);
    }

    [Fact]
    public void MarkPaid_FromPending_SetsStatusTransactionAndTimestamp()
    {
        var order = NewOrder();

        order.MarkPaid("txn_123", Now);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal("txn_123", order.PaymentTransactionId);
        Assert.Equal(Now, order.PaidAt);
    }

    [Fact]
    public void MarkPaid_Twice_Throws()
    {
        var order = NewOrder();
        order.MarkPaid("txn_1", Now);

        Assert.Throws<InvalidOrderStateException>(() => order.MarkPaid("txn_2", Now));
    }

    [Fact]
    public void MarkFulfilled_FromPaid_Succeeds()
    {
        var order = NewOrder();
        order.MarkPaid("txn", Now);

        order.MarkFulfilled(Now);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(Now, order.FulfilledAt);
    }

    [Fact]
    public void MarkFulfilled_FromPending_Throws()
    {
        Assert.Throws<InvalidOrderStateException>(() => NewOrder().MarkFulfilled(Now));
    }

    [Theory]
    [InlineData(false)] // Pending
    [InlineData(true)]  // Paid
    public void Cancel_FromPendingOrPaid_Succeeds(bool payFirst)
    {
        var order = NewOrder();
        if (payFirst)
        {
            order.MarkPaid("txn", Now);
        }

        order.Cancel(Now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_FromFulfilled_Throws()
    {
        var order = NewOrder();
        order.MarkPaid("txn", Now);
        order.MarkFulfilled(Now);

        Assert.Throws<InvalidOrderStateException>(() => order.Cancel(Now));
    }

    [Theory]
    [InlineData(false)] // Paid
    [InlineData(true)]  // Fulfilled
    public void Refund_FromPaidOrFulfilled_Succeeds(bool fulfillFirst)
    {
        var order = NewOrder();
        order.MarkPaid("txn", Now);
        if (fulfillFirst)
        {
            order.MarkFulfilled(Now);
        }

        order.Refund(Now);

        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    [Fact]
    public void Refund_FromPending_Throws()
    {
        Assert.Throws<InvalidOrderStateException>(() => NewOrder().Refund(Now));
    }

    [Fact]
    public void Cancel_FromRefunded_Throws()
    {
        var order = NewOrder();
        order.MarkPaid("txn", Now);
        order.Refund(Now);

        Assert.Throws<InvalidOrderStateException>(() => order.Cancel(Now));
    }
}
