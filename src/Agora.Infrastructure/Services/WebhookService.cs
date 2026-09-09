using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

/// <summary>Computes webhook payload signatures (hex HMAC-SHA256).</summary>
public static class WebhookSigner
{
    public static string ComputeSignature(string secret, string payload)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Deterministic in-memory webhook transport for development and tests
/// (mirrors <see cref="FakePaymentGateway"/>): endpoints whose URL contains
/// "fail" reject the delivery, everything else accepts it.
/// </summary>
public sealed class FakeWebhookSender : IWebhookSender
{
    public Task<WebhookSendResult> SendAsync(
        string url, string payload, string signature, CancellationToken ct = default)
    {
        return url.Contains("fail", StringComparison.OrdinalIgnoreCase)
            ? Task.FromResult(new WebhookSendResult(false, 500))
            : Task.FromResult(new WebhookSendResult(true, 200));
    }
}

/// <summary>
/// Fans lifecycle events out to active subscriptions: each delivery is logged
/// with its signed payload and attempt history and can be retried while under
/// the attempt cap.
/// </summary>
public class WebhookService(AgoraDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions PayloadOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>Stages durable intent and frozen deliveries; caller owns SaveChanges and its business transaction.</summary>
    public async Task<OutboxEvent> StageAsync(string eventType, object data, DateTimeOffset now, CancellationToken ct = default)
    {
        if (!WebhookEvents.IsKnown(eventType)) throw new DomainException("Unknown webhook event type.");
        var dataJson = JsonSerializer.Serialize(data, PayloadOptions);
        if (Encoding.UTF8.GetByteCount(dataJson) > 65_536) throw new DomainException("Webhook event payload exceeds 64 KiB.");
        var outbox = new OutboxEvent { EventType = eventType, SchemaVersion = 1, DataJson = dataJson, OccurredAt = now };
        db.Set<OutboxEvent>().Add(outbox);
        var subscriptions = await db.WebhookSubscriptions
            .Where(s => s.IsActive && !s.IsDeleted)
            .ToListAsync(ct);
        var matching = subscriptions.Where(s => s.SubscribesTo(eventType)).ToList();
        foreach (var subscription in matching)
            outbox.Deliveries.Add(CreateDelivery(outbox, subscription, now));
        return outbox;
    }

    public static WebhookDelivery CreateDelivery(OutboxEvent outbox, WebhookSubscription subscription, DateTimeOffset now)
    {
        var delivery = new WebhookDelivery { SubscriptionId = subscription.Id, EventId = outbox.Id,
            EventType = outbox.EventType, CreatedAt = now, DestinationUrl = subscription.Url };
        delivery.Payload = JsonSerializer.Serialize(new { id = delivery.Id, eventId = outbox.Id, schemaVersion = outbox.SchemaVersion,
            @event = outbox.EventType, createdAt = outbox.OccurredAt, data = JsonSerializer.Deserialize<JsonElement>(outbox.DataJson) }, PayloadOptions);
        if (Encoding.UTF8.GetByteCount(delivery.Payload) > 65_536) throw new DomainException("Webhook delivery envelope exceeds 64 KiB.");
        delivery.Signature = WebhookSigner.ComputeSignature(subscription.Secret, delivery.Payload); delivery.Queue(now); return delivery;
    }

    /// <summary>Re-attempts a failed delivery (409 once the attempt cap is reached).</summary>
    public async Task<WebhookDelivery> RetryAsync(Guid deliveryId, CancellationToken ct = default)
    {
        var delivery = await db.WebhookDeliveries.Include(d => d.Subscription)
            .FirstOrDefaultAsync(d => d.Id == deliveryId, ct)
            ?? throw new NotFoundException($"Webhook delivery '{deliveryId}' not found.");

        if (delivery.Status == WebhookDeliveryStatus.Succeeded)
        {
            throw new InvalidWebhookDeliveryException("Delivery has already succeeded.");
        }

        if (!delivery.CanRetry)
        {
            throw new InvalidWebhookDeliveryException(
                $"Delivery has exhausted its {WebhookDelivery.MaxAttempts} attempts.");
        }
        if (delivery.Subscription is null || !delivery.Subscription.IsActive || delivery.Subscription.IsDeleted)
            throw new InvalidWebhookDeliveryException("Delivery subscription is inactive.");

        delivery.Schedule(clock.GetUtcNow()); await db.SaveChangesAsync(ct);
        return delivery;
    }

    /// <summary>The standard order event payload.</summary>
    public static object OrderPayload(Order order) => new
    {
        orderNumber = order.Number,
        email = order.Email,
        status = order.Status.ToString(),
        currency = order.Currency,
        total = order.Total,
        createdAt = order.CreatedAt,
    };
}
