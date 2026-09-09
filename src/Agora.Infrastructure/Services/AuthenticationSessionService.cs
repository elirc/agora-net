using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed class AuthenticationSessionService(AgoraDbContext db, AuthenticationTimeProvider clock)
{
    public LoginSession Start(Customer customer, string? deviceLabel,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        var session = new LoginSession(customer.Id, customer.Role.ToString(), deviceLabel, issuedAt, expiresAt);
        db.Set<LoginSession>().Add(session);
        return session;
    }

    /// <summary>Runs after JWT signature, issuer, audience and lifetime validation.</summary>
    public async Task<bool> IsAuthorizedAsync(Guid sessionId, Guid customerId,
        string issuedRole, DateTimeOffset tokenExpiresAt, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var record = await db.Set<LoginSession>().AsNoTracking()
            .Where(s => s.Id == sessionId && s.CustomerId == customerId)
            .Join(db.Customers.AsNoTracking(), s => s.CustomerId, c => c.Id,
                (s, c) => new { Session = s, CurrentRole = c.Role })
            .SingleOrDefaultAsync(ct);

        return record is not null
            && record.Session.RevokedAt is null
            && record.Session.ExpiresAt > now
            && record.Session.ExpiresAt == tokenExpiresAt
            && string.Equals(record.Session.IssuedRole, issuedRole, StringComparison.Ordinal)
            && string.Equals(record.CurrentRole.ToString(), issuedRole, StringComparison.Ordinal);
    }
}
