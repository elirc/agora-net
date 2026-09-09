using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public class DeliveryCalendar
{
    public const int SingletonId = 1;
    public int Id { get; private set; } = SingletonId;
    public bool Enabled { get; private set; }
    public int CutoffUtcMinute { get; private set; } = 14 * 60;
    public long Revision { get; private set; }
    public List<DeliveryCalendarClosure> Closures { get; private set; } = [];
    private DeliveryCalendar() { }
    public DeliveryCalendar(bool enabled, int cutoffUtcMinute, IReadOnlyList<DateOnly> closures)
    { ReplaceCore(enabled, cutoffUtcMinute, closures); }
    public void Replace(bool enabled, int cutoffUtcMinute, IReadOnlyList<DateOnly> closures)
    { var next = checked(Revision + 1); ReplaceCore(enabled, cutoffUtcMinute, closures); Revision = next; }
    private void ReplaceCore(bool enabled, int cutoffUtcMinute, IReadOnlyList<DateOnly> closures)
    {
        if (cutoffUtcMinute is < 0 or > 1439) throw new DomainException("UTC cutoff must have minute precision within one day.");
        if (closures.Count > 366 || closures.Distinct().Count() != closures.Count)
            throw new DomainException("Closure dates must be unique and contain at most 366 dates.");
        Enabled = enabled; CutoffUtcMinute = cutoffUtcMinute;
        var wanted = closures.ToHashSet();
        Closures.RemoveAll(closure => !wanted.Contains(closure.Date));
        var existing = Closures.Select(closure => closure.Date).ToHashSet();
        foreach (var date in closures.Order().Where(date => !existing.Contains(date)))
            Closures.Add(new DeliveryCalendarClosure(SingletonId, date));
    }
}

public class DeliveryCalendarClosure
{
    public int DeliveryCalendarId { get; private set; }
    public DateOnly Date { get; private set; }
    private DeliveryCalendarClosure() { }
    public DeliveryCalendarClosure(int deliveryCalendarId, DateOnly date) { DeliveryCalendarId = deliveryCalendarId; Date = date; }
}

public sealed record DeliveryDateRange(DateTimeOffset From, DateTimeOffset To);

public static class DeliveryDateCalculator
{
    public const int MaximumSearchDays = 730;
    public static bool IsBusinessDate(DateOnly date, IReadOnlySet<DateOnly> closures) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !closures.Contains(date);
    public static DateOnly NextBusinessDate(DateOnly date, IReadOnlySet<DateOnly> closures)
    {
        for (var i = 1; i <= MaximumSearchDays; i++) { var next = date.AddDays(i); if (IsBusinessDate(next, closures)) return next; }
        throw new DomainException("No business date was found within 730 calendar days.");
    }
    public static DateOnly AddBusinessDays(DateOnly date, int days, IReadOnlySet<DateOnly> closures)
    {
        if (days < 0) throw new DomainException("Delivery days cannot be negative.");
        var current = date; var remaining = days;
        for (var i = 0; remaining > 0 && i < MaximumSearchDays; i++) { current = current.AddDays(1); if (IsBusinessDate(current, closures)) remaining--; }
        if (remaining > 0) throw new DomainException("Delivery estimate exceeds the 730-day search limit.");
        return current;
    }
    public static DeliveryDateRange Calculate(DateTimeOffset now, int minDays, int maxDays, bool enabled,
        int cutoffUtcMinute, IReadOnlySet<DateOnly> closures)
    {
        if (minDays < 0 || maxDays < minDays) throw new DomainException("Shipping delivery-day range is invalid.");
        if (!enabled) return new(now.AddDays(minDays), now.AddDays(maxDays));
        var utc = now.ToUniversalTime(); var today = DateOnly.FromDateTime(utc.UtcDateTime);
        var minute = utc.Hour * 60 + utc.Minute;
        var dispatch = IsBusinessDate(today, closures) && minute < cutoffUtcMinute ? today : NextBusinessDate(today, closures);
        static DateTimeOffset Midnight(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return new(Midnight(AddBusinessDays(dispatch, minDays, closures)), Midnight(AddBusinessDays(dispatch, maxDays, closures)));
    }
}
