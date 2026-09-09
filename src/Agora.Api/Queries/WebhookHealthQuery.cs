using System.Linq.Expressions;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Queries;

internal static class WebhookHealthQuery
{
    private sealed class Counts
    {
        public Guid Id { get; set; }
        public long Total { get; set; }
        public long Pending { get; set; }
        public long Succeeded { get; set; }
        public long Failed { get; set; }
        public long Exhausted { get; set; }
        public long Attempts { get; set; }
        public long Cancelled { get; set; }
        public long InFlight { get; set; }
        public WebhookHealthTotals Response() => new(Total, Pending, Succeeded, Failed, Exhausted, Attempts, Cancelled, InFlight);
    }

    private static readonly Expression<Func<IGrouping<Guid, WebhookDelivery>, Counts>> Projection = group => new Counts
    {
        Id = group.Key, Total = group.LongCount(),
        Pending = group.LongCount(d => d.Status == WebhookDeliveryStatus.Pending),
        Succeeded = group.LongCount(d => d.Status == WebhookDeliveryStatus.Succeeded),
        Failed = group.LongCount(d => d.Status == WebhookDeliveryStatus.Failed),
        Exhausted = group.LongCount(d => d.Status == WebhookDeliveryStatus.Failed && d.AttemptCount >= WebhookDelivery.MaxAttempts),
        Attempts = group.Sum(d => (long)d.AttemptCount),
        Cancelled = group.LongCount(d => d.Status == WebhookDeliveryStatus.Cancelled),
        InFlight = group.LongCount(d => d.Status == WebhookDeliveryStatus.InFlight),
    };

    public static async Task<WebhookHealthResponse> Read(AgoraDbContext db, DateTimeOffset asOf,
        DateTimeOffset from, DateTimeOffset to, Guid? subscriptionId, int page, int pageSize, CancellationToken ct)
    {
        var cohort = db.WebhookDeliveries.AsNoTracking().Where(d => d.CreatedAt >= from && d.CreatedAt < to);
        if (subscriptionId is { } id) cohort = cohort.Where(d => d.SubscriptionId == id);
        var overall = await cohort.GroupBy(_ => Guid.Empty).Select(Projection).SingleOrDefaultAsync(ct);
        var grouped = cohort.GroupBy(d => d.SubscriptionId).Select(Projection);
        var count = await grouped.CountAsync(ct);
        var rows = await grouped.OrderBy(g => g.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new WebhookHealthResponse(asOf, from, to, overall?.Response() ?? new WebhookHealthTotals(0, 0, 0, 0, 0, 0),
            new PagedResult<WebhookHealthSubscriptionResponse>(rows.Select(r => new WebhookHealthSubscriptionResponse(r.Id, r.Response())).ToArray(), page, pageSize, count));
    }
}
