using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record WebhookAttemptResponse(int AttemptNumber, long LeaseGeneration, DateTimeOffset ReservedAt,
    DateTimeOffset? SendInitiatedAt, DateTimeOffset? FinishedAt, string Outcome, int? HttpStatusCode, string? ReasonCode)
{
    public static WebhookAttemptResponse From(WebhookAttempt a) => new(a.AttemptNumber, a.LeaseGeneration, a.ReservedAt,
        a.SendInitiatedAt, a.FinishedAt, a.Outcome.ToString(), a.HttpStatusCode, a.ReasonCode);
}
public sealed record WebhookAttemptHistoryResponse(int HistoryStartsAtAttempt, PagedResult<WebhookAttemptResponse> Attempts);
public sealed record CreateWebhookReplayRequest([Required] Guid OperationId, [Required] Guid SubscriptionId,
    [Required, MinLength(1), MaxLength(100)] List<Guid> EventIds);
public sealed record WebhookReplayResultResponse(Guid EventId, Guid? DeliveryId, string Status);
public sealed record WebhookReplayResponse(Guid OperationId, Guid SubscriptionId, DateTimeOffset CreatedAt,
    IReadOnlyList<WebhookReplayResultResponse> Results)
{
    public static WebhookReplayResponse From(WebhookReplayBatch b) => new(b.Id, b.SubscriptionId, b.CreatedAt,
        b.Results.OrderBy(r => r.EventId).Select(r => new WebhookReplayResultResponse(r.EventId, r.DeliveryId, r.Status.ToString())).ToArray());
}
