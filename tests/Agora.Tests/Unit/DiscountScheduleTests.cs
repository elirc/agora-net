using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class DiscountScheduleTests
{
    [Fact]
    public void Schedule_is_a_predicate_over_one_supplied_instant_with_existing_guards_intact()
    {
        var start = DateTimeOffset.UnixEpoch.AddDays(10); var end = start.AddHours(1);
        var discount = new DiscountCode { StartsAt = start, ExpiresAt = end, UsageLimit = 1 };
        Assert.False(discount.IsRedeemable(start.AddTicks(-1))); Assert.True(discount.IsRedeemable(start));
        Assert.True(discount.IsRedeemable(end.AddTicks(-1))); Assert.False(discount.IsRedeemable(end));
        Assert.True(discount.IsRedeemable(start.ToOffset(TimeSpan.FromHours(-8))));
        discount.IsActive = false; Assert.False(discount.IsRedeemable(start));
        discount.IsActive = true; discount.TimesUsed = 1; Assert.False(discount.IsRedeemable(start));
        discount.TimesUsed = 0; discount.StartsAt = null; Assert.True(discount.IsRedeemable(start.AddDays(-1)));
        discount.ExpiresAt = null; Assert.True(discount.IsRedeemable(end.AddYears(1)));
    }
}
