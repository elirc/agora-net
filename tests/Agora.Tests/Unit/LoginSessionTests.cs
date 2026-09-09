using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public sealed class LoginSessionTests
{
    [Fact]
    public void Session_normalizes_label_and_has_exclusive_expiry()
    {
        var issued = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var session = new LoginSession(Guid.NewGuid(), "Customer", "  Work laptop  ", issued, issued.AddHours(1));

        Assert.Equal("Work laptop", session.DeviceLabel);
        Assert.True(session.IsActive(issued.AddHours(1).AddTicks(-1)));
        Assert.False(session.IsActive(issued.AddHours(1)));
    }

    [Fact]
    public void Revoke_is_idempotent_and_preserves_first_server_time()
    {
        var issued = DateTimeOffset.UtcNow;
        var session = new LoginSession(Guid.NewGuid(), "Customer", null, issued, issued.AddHours(1));

        session.Revoke(issued.AddMinutes(1));
        session.Revoke(issued.AddMinutes(2));

        Assert.Equal(issued.AddMinutes(1), session.RevokedAt);
        Assert.False(session.IsActive(issued.AddMinutes(1)));
    }

    [Fact]
    public void Constructor_rejects_invalid_identity_label_and_time_range()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => new LoginSession(Guid.Empty, "Customer", null, now, now.AddHours(1)));
        Assert.Throws<ArgumentException>(() => new LoginSession(Guid.NewGuid(), " ", null, now, now.AddHours(1)));
        Assert.Throws<ArgumentException>(() => new LoginSession(Guid.NewGuid(), "Customer", new string('x', 81), now, now.AddHours(1)));
        Assert.Throws<ArgumentException>(() => new LoginSession(Guid.NewGuid(), "Customer", null, now, now));
    }
}
