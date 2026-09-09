using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public class ShippingRulesService(AgoraDbContext db)
{
    public async Task EnsureEligibleAsync(Guid methodId, string country, long weightGrams, CancellationToken ct = default)
    {
        var policy = await db.Set<ShippingEligibilityPolicy>().AsNoTracking().SingleOrDefaultAsync(p => p.ShippingMethodId == methodId, ct);
        if (policy is null) return;
        var result = ShippingEligibilityRules.Evaluate(policy.Countries(), policy.MaximumWeightGrams, country, weightGrams);
        if (!result.Eligible) throw new InvalidShippingMethodException("Shipping method is not eligible: " + string.Join(", ", result.Reasons) + ".");
    }
    public async Task<DeliveryDateRange> DeliveryDatesAsync(DateTimeOffset now, ShippingMethod method, CancellationToken ct = default)
    {
        var calendar = await db.Set<DeliveryCalendar>().AsNoTracking().Include(c => c.Closures).SingleOrDefaultAsync(c => c.Id == DeliveryCalendar.SingletonId, ct);
        return calendar is null
            ? DeliveryDateCalculator.Calculate(now, method.MinDays, method.MaxDays, false, 840, new HashSet<DateOnly>())
            : DeliveryDateCalculator.Calculate(now, method.MinDays, method.MaxDays, calendar.Enabled, calendar.CutoffUtcMinute,
                calendar.Closures.Select(c => c.Date).ToHashSet());
    }
}
