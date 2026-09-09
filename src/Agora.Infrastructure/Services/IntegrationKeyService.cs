using System.Security.Cryptography;
using System.Text;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record IntegrationKeyIssue(IntegrationApiKey Key, string Token);

public sealed class IntegrationKeyService(AgoraDbContext db, AuthenticationTimeProvider clock)
{
    public async Task<IntegrationKeyIssue> IssueAsync(string name, IReadOnlyList<string> scopes, int expiryDays, Guid creatorId, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = $"{id:N}.{secret}";
        var key = new IntegrationApiKey(id, name, Digest(token), IntegrationApiKey.ParseScopes(scopes), creatorId, clock.GetUtcNow(), expiryDays);
        db.Set<IntegrationApiKey>().Add(key);
        await db.SaveChangesAsync(ct);
        return new(key, token);
    }
    public async Task<IntegrationApiKey?> AuthenticateAsync(string token, CancellationToken ct)
    {
        if (token.Length != 76 || token[32] != '.' || !Guid.TryParseExact(token[..32], "N", out var id)) return null;
        var key = await db.Set<IntegrationApiKey>().AsNoTracking().SingleOrDefaultAsync(k => k.Id == id, ct);
        return key is not null && key.IsActive(clock.GetUtcNow()) && CryptographicOperations.FixedTimeEquals(key.SecretDigest, Digest(token)) ? key : null;
    }
    private static byte[] Digest(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
