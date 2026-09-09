namespace Agora.Domain.Entities;

/// <summary>Digest-only capability bound to one guest order.</summary>
public sealed class GuestOrderCredential
{
    private GuestOrderCredential() { }

    public GuestOrderCredential(Guid orderId, byte[] secretDigest, DateTimeOffset issuedAt,
        DateTimeOffset expiresAt, Guid? issuedByAdminId = null, Guid? id = null)
    {
        if (orderId == Guid.Empty) throw new ArgumentException("An order is required.", nameof(orderId));
        if (secretDigest is not { Length: 32 }) throw new ArgumentException("A SHA-256 digest is required.", nameof(secretDigest));
        if (expiresAt <= issuedAt) throw new ArgumentException("Expiry must follow issue time.", nameof(expiresAt));
        Id = id ?? Guid.NewGuid(); OrderId = orderId; SecretDigest = secretDigest.ToArray();
        IssuedAt = issuedAt; ExpiresAt = expiresAt; IssuedByAdminId = issuedByAdminId;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public byte[] SecretDigest { get; private set; } = [];
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? IssuedByAdminId { get; private set; }
    public Guid? RevokedByAdminId { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
    public void Revoke(DateTimeOffset now, Guid? adminId)
    { if (RevokedAt is null) { RevokedAt = now; RevokedByAdminId = adminId; } }
}
