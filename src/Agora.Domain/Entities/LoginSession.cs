namespace Agora.Domain.Entities;

/// <summary>
/// Server-side authorization record for one issued JWT. The signed token proves
/// where the claims came from; this record decides whether they are still usable.
/// Raw bearer tokens are deliberately never persisted.
/// </summary>
public sealed class LoginSession
{
    private LoginSession() { }

    public LoginSession(Guid customerId, string issuedRole, string? deviceLabel,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("A customer is required.", nameof(customerId));
        if (string.IsNullOrWhiteSpace(issuedRole)) throw new ArgumentException("An issued role is required.", nameof(issuedRole));
        if (expiresAt <= issuedAt) throw new ArgumentException("Expiry must be after issue time.", nameof(expiresAt));

        Id = Guid.NewGuid();
        CustomerId = customerId;
        IssuedRole = issuedRole.Trim();
        DeviceLabel = NormalizeDeviceLabel(deviceLabel);
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string IssuedRole { get; private set; } = string.Empty;
    public string? DeviceLabel { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now)
    {
        if (RevokedAt is null) RevokedAt = now;
    }

    private static string? NormalizeDeviceLabel(string? value)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (normalized.Length == 0) return null;
        if (normalized.Length > 80) throw new ArgumentException("Device label cannot exceed 80 characters.", nameof(value));
        return normalized;
    }
}
