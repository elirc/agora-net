using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public sealed class GuestOrderCredentialTests
{
    [Fact]
    public void Expiry_is_exclusive_and_revocation_preserves_first_audit_fact()
    {
        var now = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        var credential = new GuestOrderCredential(Guid.NewGuid(), new byte[32], now, now.AddDays(30));
        var admin = Guid.NewGuid();
        Assert.True(credential.IsActive(now.AddDays(30).AddTicks(-1)));
        Assert.False(credential.IsActive(now.AddDays(30)));
        credential.Revoke(now.AddDays(1), admin);
        credential.Revoke(now.AddDays(2), Guid.NewGuid());
        Assert.Equal(now.AddDays(1), credential.RevokedAt);
        Assert.Equal(admin, credential.RevokedByAdminId);
    }

    [Fact]
    public void Constructor_requires_order_sha256_digest_and_forward_time()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => new GuestOrderCredential(Guid.Empty, new byte[32], now, now.AddDays(1)));
        Assert.Throws<ArgumentException>(() => new GuestOrderCredential(Guid.NewGuid(), new byte[31], now, now.AddDays(1)));
        Assert.Throws<ArgumentException>(() => new GuestOrderCredential(Guid.NewGuid(), new byte[32], now, now));
    }
}
