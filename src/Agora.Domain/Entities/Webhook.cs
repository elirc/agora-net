namespace Agora.Domain.Entities;

/// <summary>Well-known webhook event names.</summary>
public static class WebhookEvents
{
    public const string OrderCreated = "order.created";
    public const string OrderPaid = "order.paid";
    public const string OrderFulfilled = "order.fulfilled";
    public const string OrderRefunded = "order.refunded";

    public static readonly IReadOnlyList<string> All =
        [OrderCreated, OrderPaid, OrderFulfilled, OrderRefunded];

    public static bool IsKnown(string eventType) =>
        All.Contains(eventType, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// An endpoint subscribed to order lifecycle events. Payloads are signed with
/// HMAC-SHA256 using <see cref="Secret"/> (X-Agora-Signature).
/// </summary>
public class WebhookSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public List<string> Events { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool SubscribesTo(string eventType) =>
        Events.Contains(eventType, StringComparer.OrdinalIgnoreCase);
}

public enum WebhookDeliveryStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}

/// <summary>One event delivery to one subscription, with its attempt history.</summary>
public class WebhookDelivery
{
    public const int MaxAttempts = 5;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriptionId { get; set; }
    public WebhookSubscription? Subscription { get; set; }

    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;

    /// <summary>Hex HMAC-SHA256 of the payload, sent as X-Agora-Signature.</summary>
    public string Signature { get; set; } = string.Empty;

    public WebhookDeliveryStatus Status { get; private set; } = WebhookDeliveryStatus.Pending;
    public int AttemptCount { get; private set; }
    public int? LastResponseStatusCode { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool CanRetry => Status == WebhookDeliveryStatus.Failed && AttemptCount < MaxAttempts;

    public void RecordAttempt(bool success, int? responseStatusCode, DateTimeOffset now)
    {
        AttemptCount++;
        LastResponseStatusCode = responseStatusCode;
        LastAttemptAt = now;
        Status = success ? WebhookDeliveryStatus.Succeeded : WebhookDeliveryStatus.Failed;
    }
}
