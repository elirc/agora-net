using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

/// <summary>
/// Exhaustive transition matrix for the order state machine: every
/// (state, action) pair is pinned as either legal or an
/// <see cref="InvalidOrderStateException"/>.
/// </summary>
public class OrderStateMatrixTests
{
    public enum OrderAction
    {
        MarkPaid,
        MarkPartiallyFulfilled,
        MarkFulfilled,
        Cancel,
        Refund,
    }

    /// <summary>The full legal-transition table; everything else must throw.</summary>
    private static readonly Dictionary<OrderAction, OrderStatus[]> LegalFrom = new()
    {
        [OrderAction.MarkPaid] = [OrderStatus.Pending],
        [OrderAction.MarkPartiallyFulfilled] = [OrderStatus.Paid, OrderStatus.PartiallyFulfilled],
        [OrderAction.MarkFulfilled] = [OrderStatus.Paid, OrderStatus.PartiallyFulfilled],
        [OrderAction.Cancel] = [OrderStatus.Pending, OrderStatus.Paid],
        [OrderAction.Refund] =
            [OrderStatus.Paid, OrderStatus.PartiallyFulfilled, OrderStatus.Fulfilled],
    };

    public static TheoryData<OrderStatus, OrderAction> AllPairs()
    {
        var data = new TheoryData<OrderStatus, OrderAction>();
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            foreach (var action in Enum.GetValues<OrderAction>())
            {
                data.Add(status, action);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Transition_MatchesLegalTable(OrderStatus from, OrderAction action)
    {
        var order = OrderIn(from);

        if (LegalFrom[action].Contains(from))
        {
            Apply(order, action);
            Assert.Equal(TargetOf(action), order.Status);
        }
        else
        {
            var ex = Assert.Throws<InvalidOrderStateException>(() => Apply(order, action));
            Assert.Contains(from.ToString(), ex.Message);
        }
    }

    [Fact]
    public void LegalTable_CoversEveryAction()
    {
        Assert.All(Enum.GetValues<OrderAction>(), action => Assert.True(LegalFrom.ContainsKey(action)));
    }

    private static OrderStatus TargetOf(OrderAction action) => action switch
    {
        OrderAction.MarkPaid => OrderStatus.Paid,
        OrderAction.MarkPartiallyFulfilled => OrderStatus.PartiallyFulfilled,
        OrderAction.MarkFulfilled => OrderStatus.Fulfilled,
        OrderAction.Cancel => OrderStatus.Cancelled,
        OrderAction.Refund => OrderStatus.Refunded,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Order OrderIn(OrderStatus status)
    {
        var order = new Order { Number = "ORD-STATE-1" };
        switch (status)
        {
            case OrderStatus.Pending:
                break;
            case OrderStatus.Paid:
                order.MarkPaid("txn_state", Now);
                break;
            case OrderStatus.PartiallyFulfilled:
                order.MarkPaid("txn_state", Now);
                order.MarkPartiallyFulfilled();
                break;
            case OrderStatus.Fulfilled:
                order.MarkPaid("txn_state", Now);
                order.MarkFulfilled(Now);
                break;
            case OrderStatus.Cancelled:
                order.MarkPaid("txn_state", Now);
                order.Cancel(Now);
                break;
            case OrderStatus.Refunded:
                order.MarkPaid("txn_state", Now);
                order.Refund(Now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        Assert.Equal(status, order.Status);
        return order;
    }

    private static void Apply(Order order, OrderAction action)
    {
        switch (action)
        {
            case OrderAction.MarkPaid:
                order.MarkPaid("txn_next", Now);
                break;
            case OrderAction.MarkPartiallyFulfilled:
                order.MarkPartiallyFulfilled();
                break;
            case OrderAction.MarkFulfilled:
                order.MarkFulfilled(Now);
                break;
            case OrderAction.Cancel:
                order.Cancel(Now);
                break;
            case OrderAction.Refund:
                order.Refund(Now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }
}
