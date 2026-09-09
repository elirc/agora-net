using System.Security.Cryptography;
using System.Text;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record OrderAccessActor(Guid? CustomerId, bool IsAdmin, string? GuestToken);
public sealed record GuestCredentialIssue(GuestOrderCredential Credential, string Token);

public sealed class GuestOrderAccessService(AgoraDbContext db, TimeProvider clock)
{
    public GuestCredentialIssue Issue(Order order, Guid? adminId = null)
    {
        if (order.CustomerId is not null) throw new InvalidOrderStateException("Account-owned orders do not use guest credentials.");
        var now = clock.GetUtcNow();
        var id = Guid.NewGuid();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = $"{id:N}.{secret}";
        var credential = new GuestOrderCredential(order.Id, Digest(token), now, now.AddDays(30), adminId, id);
        db.Set<GuestOrderCredential>().Add(credential);
        return new GuestCredentialIssue(credential, token);
    }

    public async Task EnsureCanReadAsync(Order order, OrderAccessActor actor, CancellationToken ct)
    {
        if (actor.IsAdmin || order.CustomerId is { } owner && actor.CustomerId == owner) return;
        if (order.CustomerId is null && await HasValidGuestTokenAsync(order.Id, actor.GuestToken, ct)) return;
        throw new NotFoundException($"Order '{order.Number}' not found.");
    }

    public async Task<GuestCredentialIssue> RotateAsync(Order order, Guid adminId, CancellationToken ct)
    {
        if (order.CustomerId is not null) throw new InvalidOrderStateException("Account-owned orders do not use guest credentials.");
        var now = clock.GetUtcNow();
        var active = await db.Set<GuestOrderCredential>()
            .Where(c => c.OrderId == order.Id && c.RevokedAt == null).ToArrayAsync(ct);
        foreach (var credential in active) credential.Revoke(now, adminId);
        // Flush revocations before inserting the replacement so SQLite's partial
        // unique index never observes two active rows. The caller owns the outer
        // transaction, so this ordering remains one atomic rotation.
        await db.SaveChangesAsync(ct);
        return Issue(order, adminId);
    }

    private async Task<bool> HasValidGuestTokenAsync(Guid orderId, string? token, CancellationToken ct)
    {
        if (!TryReadId(token, out var id)) return false;
        var credential = await db.Set<GuestOrderCredential>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id && c.OrderId == orderId, ct);
        return credential is not null && credential.IsActive(clock.GetUtcNow())
            && CryptographicOperations.FixedTimeEquals(credential.SecretDigest, Digest(token!));
    }

    private static bool TryReadId(string? token, out Guid id)
    {
        id = default;
        if (token is null || token.Length > 100) return false;
        var dot = token.IndexOf('.');
        return dot == 32 && token.LastIndexOf('.') == dot && Guid.TryParseExact(token[..dot], "N", out id)
            && token.Length - dot - 1 == 43;
    }

    private static byte[] Digest(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
