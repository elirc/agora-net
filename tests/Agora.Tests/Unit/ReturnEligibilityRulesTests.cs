using Agora.Domain.Entities;
using Agora.Domain.Services;

namespace Agora.Tests.Unit;

public class ReturnEligibilityRulesTests
{
    [Fact]
    public void Time_window_is_exclusive_and_disabled_policy_does_not_require_historical_stamp()
    {
        var fulfilled = DateTimeOffset.UnixEpoch; var deadline = fulfilled.AddDays(30);
        Assert.Empty(ReturnEligibilityRules.Evaluate(OrderStatus.Fulfilled, fulfilled, 30, deadline.AddTicks(-1)).Reasons);
        Assert.Contains("ReturnWindowExpired", ReturnEligibilityRules.Evaluate(OrderStatus.Fulfilled, fulfilled, 30, deadline).Reasons);
        Assert.Contains("ReturnWindowExpired", ReturnEligibilityRules.Evaluate(OrderStatus.Fulfilled, fulfilled, 30, deadline.AddTicks(1)).Reasons);
        Assert.Empty(ReturnEligibilityRules.Evaluate(OrderStatus.Fulfilled, null, null, deadline).Reasons);
        Assert.Contains("MissingFulfilledAt", ReturnEligibilityRules.Evaluate(OrderStatus.Fulfilled, null, 30, deadline).Reasons);
        Assert.Contains("OrderNotFulfilled", ReturnEligibilityRules.Evaluate(OrderStatus.PartiallyFulfilled, fulfilled, null, deadline).Reasons);
        Assert.Throws<ArgumentOutOfRangeException>(() => ReturnEligibilityRules.Evaluate(OrderStatus.Fulfilled, fulfilled, 0, deadline));
    }
}
