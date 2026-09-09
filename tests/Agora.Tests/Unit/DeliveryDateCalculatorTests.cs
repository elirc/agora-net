using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class DeliveryDateCalculatorTests
{
    private static readonly HashSet<DateOnly> MondayClosed = [new(2026, 9, 14)];

    [Fact]
    public void Before_friday_cutoff_dispatches_friday_and_one_business_day_is_tuesday()
    {
        var result = DeliveryDateCalculator.Calculate(new DateTimeOffset(2026, 9, 11, 13, 59, 0, TimeSpan.Zero), 0, 1, true, 14 * 60, MondayClosed);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero), result.From);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero), result.To);
    }
    [Fact]
    public void Exact_cutoff_moves_dispatch_to_tuesday_and_day_one_to_wednesday()
    {
        var result = DeliveryDateCalculator.Calculate(new DateTimeOffset(2026, 9, 11, 14, 0, 0, TimeSpan.Zero), 0, 1, true, 14 * 60, MondayClosed);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero), result.From);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero), result.To);
    }
    [Theory]
    [InlineData("2028-02-29", "2028-03-01")]
    [InlineData("2026-12-31", "2027-01-01")]
    public void Business_day_addition_crosses_calendar_boundaries(string start, string expected)
    {
        var actual = DeliveryDateCalculator.AddBusinessDays(DateOnly.Parse(start), 1, new HashSet<DateOnly>());
        Assert.Equal(DateOnly.Parse(expected), actual);
    }
    [Fact]
    public void Disabled_mode_preserves_existing_elapsed_day_semantics_and_instant()
    {
        var now = new DateTimeOffset(2026, 9, 11, 23, 17, 0, TimeSpan.Zero);
        var result = DeliveryDateCalculator.Calculate(now, 1, 3, false, 0, new HashSet<DateOnly>());
        Assert.Equal(now.AddDays(1), result.From); Assert.Equal(now.AddDays(3), result.To);
    }
    [Fact]
    public void Search_is_bounded_when_every_candidate_is_closed()
    {
        var start = new DateOnly(2026, 1, 1);
        var closures = Enumerable.Range(1, 730).Select(start.AddDays).ToHashSet();
        Assert.Throws<DomainException>(() => DeliveryDateCalculator.NextBusinessDate(start, closures));
    }
}
