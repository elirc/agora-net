using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record SaveWebhookSubscriptionRequest(
    [Required, MaxLength(2000), Url] string Url,
    [Required, MinLength(16), MaxLength(200)] string Secret,
    [Required, MinLength(1)] List<string> Events,
    bool? IsActive);

public sealed record WebhookSubscriptionResponse(
    Guid Id,
    string Url,
    IReadOnlyList<string> Events,
    bool IsActive,
    DateTimeOffset CreatedAt)
{
    // The secret is write-only; it never leaves the API.
    public static WebhookSubscriptionResponse From(WebhookSubscription subscription) => new(
        subscription.Id,
        subscription.Url,
        subscription.Events,
        subscription.IsActive,
        subscription.CreatedAt);
}

public sealed record WebhookDeliveryResponse(
    Guid Id,
    Guid SubscriptionId,
    string EventType,
    string Payload,
    string Signature,
    string Status,
    int AttemptCount,
    int? LastResponseStatusCode,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset CreatedAt)
{
    public Guid? EventId { get; init; }
    public int HistoryStartsAtAttempt { get; init; }
    public static WebhookDeliveryResponse From(WebhookDelivery delivery) => new(
        delivery.Id,
        delivery.SubscriptionId,
        delivery.EventType,
        delivery.Payload,
        delivery.Signature,
        delivery.Status.ToString(),
        delivery.AttemptCount,
        delivery.LastResponseStatusCode,
        delivery.LastAttemptAt,
        delivery.CreatedAt) { EventId = delivery.EventId, HistoryStartsAtAttempt = delivery.HistoryStartsAtAttempt };
}
