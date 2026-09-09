using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public class ReturnEvidence
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ReturnRequestId { get; private set; }
    public Guid AuthorCustomerId { get; private set; }
    public string Url { get; private set; } = "";
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    private ReturnEvidence() { }
    public ReturnEvidence(Guid returnId, Guid author, string url, string? description, DateTimeOffset now)
    {
        var normalized = url.Trim(); var text = description?.Trim();
        if (normalized.Length is < 1 or > 2000 || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new DomainException("Evidence must use an absolute HTTPS URL without user-info credentials, at most 2,000 characters.");
        if (text?.Length > 200) throw new DomainException("Evidence description must contain at most 200 characters.");
        ReturnRequestId = returnId; AuthorCustomerId = author; Url = normalized;
        Description = string.IsNullOrEmpty(text) ? null : text; CreatedAt = now;
    }
}
