namespace Agora.Domain.Services;

public sealed record WebhookSendResult(bool Success, int? StatusCode);

/// <summary>HTTP transport for webhook deliveries; production would POST for real.</summary>
public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(
        string url, string payload, string signature, CancellationToken ct = default);
}
