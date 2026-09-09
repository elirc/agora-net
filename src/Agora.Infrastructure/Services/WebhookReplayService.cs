using System.Security.Cryptography;
using System.Text;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public class WebhookReplayService(AgoraDbContext db, TimeProvider clock)
{
    public async Task<WebhookReplayBatch> ReplayAsync(Guid operationId, Guid subscriptionId, IReadOnlyList<Guid> requestedEventIds,
        Guid requester, CancellationToken ct = default)
    {
        if (operationId == Guid.Empty) throw new InvalidWebhookReplayException("Operation ID cannot be empty.");
        var eventIds = requestedEventIds.Order().ToArray();
        if (eventIds.Length is < 1 or > 100 || eventIds.Distinct().Count() != eventIds.Length)
            throw new InvalidWebhookReplayException("Supply 1..100 distinct event IDs.");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subscriptionId + ":" + string.Join(',', eventIds)))).ToLowerInvariant();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var prior = await db.Set<WebhookReplayBatch>().AsNoTracking().Include(b => b.Results).SingleOrDefaultAsync(b => b.Id == operationId, ct);
        if (prior is not null)
        {
            if (prior.RequestDigest != digest) throw new InvalidWebhookDeliveryException("Replay operation ID was already used for different content.");
            return prior;
        }
        var now = clock.GetUtcNow();
        var subscription = await db.WebhookSubscriptions.SingleOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw new NotFoundException("Target webhook subscription was not found.");
        if (!subscription.IsActive || subscription.IsDeleted) throw new InvalidWebhookDeliveryException("Target webhook subscription is inactive.");
        var events = await db.Set<OutboxEvent>().Where(e => eventIds.Contains(e.Id)).ToListAsync(ct);
        if (events.Count != eventIds.Length) throw new InvalidWebhookReplayException("One or more retained events do not exist.");
        if (events.Any(e => e.SchemaVersion != 1 || e.OccurredAt < now.AddDays(-30) || !subscription.SubscribesTo(e.EventType)))
            throw new InvalidWebhookReplayException("Every event must be version 1, retained within 30 days, and subscribed by the target.");
        var existing = await db.WebhookDeliveries.Where(d => d.SubscriptionId == subscriptionId && d.EventId != null && eventIds.Contains(d.EventId.Value))
            .ToDictionaryAsync(d => d.EventId!.Value, ct);
        var batch = new WebhookReplayBatch { Id = operationId, SubscriptionId = subscriptionId, RequestDigest = digest, RequestedBy = requester, CreatedAt = now };
        foreach (var outbox in events.OrderBy(e => e.Id))
        {
            if (existing.TryGetValue(outbox.Id, out var old))
                batch.Results.Add(new WebhookReplayResult { BatchId = batch.Id, EventId = outbox.Id, DeliveryId = old.Id, Status = WebhookReplayResultStatus.AlreadyExists });
            else
            {
                var delivery = WebhookService.CreateDelivery(outbox, subscription, now); db.WebhookDeliveries.Add(delivery);
                batch.Results.Add(new WebhookReplayResult { BatchId = batch.Id, EventId = outbox.Id, DeliveryId = delivery.Id, Status = WebhookReplayResultStatus.Enqueued });
            }
        }
        db.Set<WebhookReplayBatch>().Add(batch); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return batch;
    }
}
