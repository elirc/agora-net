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
public class WebhookService(AgoraDbContext db, IWebhookSender sender)
{
    private static readonly JsonSerializerOptions PayloadOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>Creates and immediately attempts one delivery per subscribed endpoint.</summary>
    public async Task DispatchAsync(string eventType, object data, CancellationToken ct = default)
    {
        var subscriptions = await db.WebhookSubscriptions
            .Where(s => s.IsActive)
            .ToListAsync(ct);
        var matching = subscriptions.Where(s => s.SubscribesTo(eventType)).ToList();
        if (matching.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var subscription in matching)
        {
            var delivery = new WebhookDelivery
            {
                SubscriptionId = subscription.Id,
                EventType = eventType,
                CreatedAt = now,
            };
            delivery.Payload = JsonSerializer.Serialize(new
            {
                id = delivery.Id,
                @event = eventType,
                createdAt = now,
                data,
            }, PayloadOptions);
            delivery.Signature = WebhookSigner.ComputeSignature(subscription.Secret, delivery.Payload);

            var result = await sender.SendAsync(
                subscription.Url, delivery.Payload, delivery.Signature, ct);
            delivery.RecordAttempt(result.Success, result.StatusCode, now);

            db.WebhookDeliveries.Add(delivery);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Re-attempts a failed delivery (409 once the attempt cap is reached).</summary>
    public async Task<WebhookDelivery> RetryAsync(Guid deliveryId, CancellationToken ct = default)
    {
        var delivery = await db.WebhookDeliveries
            .Include(d => d.Subscription)
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

        var result = await sender.SendAsync(
            delivery.Subscription!.Url, delivery.Payload, delivery.Signature, ct);
        delivery.RecordAttempt(result.Success, result.StatusCode, DateTimeOffset.UtcNow);

        await db.SaveChangesAsync(ct);
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
