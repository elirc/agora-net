namespace Agora.Api.Contracts;

public sealed record WebhookHealthTotals(long Total, long Pending, long Succeeded, long Failed,
    long ExhaustedFailed, long CohortLifetimeAttemptCount, long Cancelled = 0, long InFlight = 0)
{
    public decimal? SuccessRatio => Total == 0 ? null : (decimal)Succeeded / Total;
}
public sealed record WebhookHealthSubscriptionResponse(Guid SubscriptionId, WebhookHealthTotals Totals);
public sealed record WebhookHealthResponse(DateTimeOffset AsOf, DateTimeOffset From, DateTimeOffset To,
    WebhookHealthTotals Overall, PagedResult<WebhookHealthSubscriptionResponse> Subscriptions);
