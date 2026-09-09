using Agora.Domain.Common;

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
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool SubscribesTo(string eventType) =>
        IsActive && !IsDeleted && Events.Contains(eventType, StringComparer.OrdinalIgnoreCase);
    public bool SoftDelete(DateTimeOffset now)
    {
        if (IsDeleted) return false;
        IsActive = false; IsDeleted = true; DeletedAt = now; return true;
    }
}

public enum WebhookDeliveryStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    InFlight = 3,
    Cancelled = 4,
}

/// <summary>One event delivery to one subscription, with its attempt history.</summary>
public class WebhookDelivery
{
    public const int MaxAttempts = 5;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriptionId { get; set; }
    public WebhookSubscription? Subscription { get; set; }

    public string EventType { get; set; } = string.Empty;
    public Guid? EventId { get; set; }
    public OutboxEvent? Event { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string DestinationUrl { get; set; } = string.Empty;

    /// <summary>Hex HMAC-SHA256 of the payload, sent as X-Agora-Signature.</summary>
    public string Signature { get; set; } = string.Empty;

    public WebhookDeliveryStatus Status { get; private set; } = WebhookDeliveryStatus.Pending;
    public int AttemptCount { get; private set; }
    public int? LastResponseStatusCode { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DueAt { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public long LeaseGeneration { get; private set; }
    public long Revision { get; private set; }
    public int HistoryStartsAtAttempt { get; set; } = 1;

    public bool CanRetry => Status == WebhookDeliveryStatus.Failed && AttemptCount < MaxAttempts;

    public WebhookAttempt Claim(DateTimeOffset now)
    {
        if (Status is not (WebhookDeliveryStatus.Pending or WebhookDeliveryStatus.Failed) || DueAt is null || DueAt > now)
            throw new InvalidWebhookDeliveryException("Delivery is not due for claim.");
        if (AttemptCount >= MaxAttempts) throw new InvalidWebhookDeliveryException("Delivery has exhausted its attempt slots.");
        var nextAttempt = checked(AttemptCount + 1); var nextGeneration = checked(LeaseGeneration + 1); var nextRevision = checked(Revision + 1);
        AttemptCount = nextAttempt; LeaseGeneration = nextGeneration; Revision = nextRevision;
        Status = WebhookDeliveryStatus.InFlight; LeaseExpiresAt = now.AddSeconds(60); DueAt = null;
        return new WebhookAttempt(Id, AttemptCount, LeaseGeneration, now);
    }

    public bool Complete(long generation, bool success, int? statusCode, DateTimeOffset now)
    {
        if (Status != WebhookDeliveryStatus.InFlight || LeaseGeneration != generation || LeaseExpiresAt <= now) return false;
        var nextRevision = checked(Revision + 1);
        Status = success ? WebhookDeliveryStatus.Succeeded : WebhookDeliveryStatus.Failed;
        LastResponseStatusCode = statusCode; LastAttemptAt = now; LeaseExpiresAt = null; Revision = nextRevision;
        DueAt = success || AttemptCount >= MaxAttempts ? null : now.Add(RetryDelay(AttemptCount)); return true;
    }

    public bool ExpireLease(DateTimeOffset now)
    {
        if (Status != WebhookDeliveryStatus.InFlight || LeaseExpiresAt > now) return false;
        var nextRevision = checked(Revision + 1);
        Status = WebhookDeliveryStatus.Failed; LastResponseStatusCode = null; LastAttemptAt = now; LeaseExpiresAt = null;
        DueAt = AttemptCount >= MaxAttempts ? null : now; Revision = nextRevision; return true;
    }

    public void Schedule(DateTimeOffset now)
    {
        if (!CanRetry) throw new InvalidWebhookDeliveryException("Delivery cannot be scheduled.");
        var nextRevision = checked(Revision + 1); Status = WebhookDeliveryStatus.Failed; DueAt = now; Revision = nextRevision;
    }
    public void Queue(DateTimeOffset now) { DueAt = now; }
    public void Cancel()
    {
        if ((Status is WebhookDeliveryStatus.Pending or WebhookDeliveryStatus.Failed) && DueAt is not null)
        { var nextRevision = checked(Revision + 1); Status = WebhookDeliveryStatus.Cancelled; DueAt = null; Revision = nextRevision; }
    }
    private static TimeSpan RetryDelay(int attempt) => attempt switch { 1 => TimeSpan.FromMinutes(1), 2 => TimeSpan.FromMinutes(5), 3 => TimeSpan.FromMinutes(15), _ => TimeSpan.FromMinutes(60) };

    public void RecordAttempt(bool success, int? responseStatusCode, DateTimeOffset now)
    {
        AttemptCount++;
        LastResponseStatusCode = responseStatusCode;
        LastAttemptAt = now;
        Status = success ? WebhookDeliveryStatus.Succeeded : WebhookDeliveryStatus.Failed;
    }
}

public class OutboxEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string DataJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public List<WebhookDelivery> Deliveries { get; set; } = [];
}

public enum WebhookAttemptOutcome { Pending = 0, Succeeded = 1, Failed = 2, Unknown = 3 }
public class WebhookAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeliveryId { get; private set; }
    public int AttemptNumber { get; private set; }
    public long LeaseGeneration { get; private set; }
    public DateTimeOffset ReservedAt { get; private set; }
    public DateTimeOffset? SendInitiatedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public WebhookAttemptOutcome Outcome { get; private set; }
    public int? HttpStatusCode { get; private set; }
    public string? ReasonCode { get; private set; }
    private WebhookAttempt() { }
    public WebhookAttempt(Guid deliveryId, int number, long generation, DateTimeOffset now)
    { DeliveryId = deliveryId; AttemptNumber = number; LeaseGeneration = generation; ReservedAt = now; }
    public bool MarkSendInitiated(DateTimeOffset now)
    {
        if (Outcome != WebhookAttemptOutcome.Pending || SendInitiatedAt is not null) return false;
        SendInitiatedAt = now; return true;
    }
    public bool Finish(WebhookAttemptOutcome outcome, DateTimeOffset now, int? httpStatusCode = null, string? reasonCode = null)
    {
        if (Outcome != WebhookAttemptOutcome.Pending) return false;
        if (reasonCode?.Length > 64) throw new ArgumentException("Reason code exceeds 64 characters.");
        Outcome = outcome; FinishedAt = now; HttpStatusCode = httpStatusCode; ReasonCode = reasonCode; return true;
    }
}

public class WebhookReplayBatch
{
    public Guid Id { get; set; }
    public Guid SubscriptionId { get; set; }
    public string RequestDigest { get; set; } = string.Empty;
    public Guid RequestedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<WebhookReplayResult> Results { get; set; } = [];
}
public enum WebhookReplayResultStatus { Enqueued = 0, AlreadyExists = 1 }
public class WebhookReplayResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public Guid EventId { get; set; }
    public Guid? DeliveryId { get; set; }
    public WebhookReplayResultStatus Status { get; set; }
}
public sealed class InvalidWebhookReplayException(string message) : Agora.Domain.Common.DomainException(message);
