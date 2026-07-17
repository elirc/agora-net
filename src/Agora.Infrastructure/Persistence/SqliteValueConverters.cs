using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Agora.Infrastructure.Persistence;

/// <summary>
/// SQLite cannot order/compare DateTimeOffset natively, so store as UTC ticks
/// (long). Ordering by the column then matches chronological order exactly.
/// </summary>
public sealed class DateTimeOffsetToUtcTicksConverter : ValueConverter<DateTimeOffset, long>
{
    public DateTimeOffsetToUtcTicksConverter()
        : base(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero))
    {
    }
}

/// <summary>
/// SQLite cannot order/compare decimal natively either (it is stored as TEXT),
/// so store money/decimal values as integer cents.
/// </summary>
public sealed class DecimalToCentsConverter : ValueConverter<decimal, long>
{
    public DecimalToCentsConverter()
        : base(
            v => (long)Math.Round(v * 100m, MidpointRounding.AwayFromZero),
            v => v / 100m)
    {
    }
}

/// <summary>
/// Fractional rates (e.g. a 0.095 tax rate) need more precision than cents;
/// store them as integer millionths.
/// </summary>
public sealed class DecimalRateToMillionthsConverter : ValueConverter<decimal, long>
{
    public DecimalRateToMillionthsConverter()
        : base(
            v => (long)Math.Round(v * 1_000_000m, MidpointRounding.AwayFromZero),
            v => v / 1_000_000m)
    {
    }
}
