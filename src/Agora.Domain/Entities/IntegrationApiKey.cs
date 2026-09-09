using Agora.Domain.Common;

namespace Agora.Domain.Entities;

[Flags]
public enum IntegrationKeyScope { CatalogRead = 1, InventoryRead = 2 }

public sealed class IntegrationApiKey
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public byte[] SecretDigest { get; private set; } = [];
    public IntegrationKeyScope Scopes { get; private set; }
    public Guid CreatorId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    private IntegrationApiKey() { }
    public IntegrationApiKey(Guid id, string name, byte[] digest, IntegrationKeyScope scopes,
        Guid creatorId, DateTimeOffset now, int expiryDays)
    {
        var normalized = name.Trim();
        if (id == Guid.Empty || creatorId == Guid.Empty || normalized.Length is < 1 or > 80 || digest.Length != 32
            || expiryDays is < 1 or > 90 || scopes == 0 || (scopes & ~(IntegrationKeyScope.CatalogRead | IntegrationKeyScope.InventoryRead)) != 0)
            throw new DomainException("Integration key metadata is invalid.");
        Id = id; Name = normalized; SecretDigest = digest.ToArray(); Scopes = scopes;
        CreatorId = creatorId; CreatedAt = now; ExpiresAt = now.AddDays(expiryDays);
    }
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
    public static IntegrationKeyScope ParseScopes(IReadOnlyList<string> values)
    {
        if (values.Count is < 1 or > 2) throw new DomainException("Supply one or both supported read scopes.");
        IntegrationKeyScope result = 0;
        foreach (var value in values)
        {
            var normalized = value?.Trim();
            var scope = string.Equals(normalized, "CatalogRead", StringComparison.OrdinalIgnoreCase) ? IntegrationKeyScope.CatalogRead
                : string.Equals(normalized, "InventoryRead", StringComparison.OrdinalIgnoreCase) ? IntegrationKeyScope.InventoryRead : 0;
            if (scope == 0 || result.HasFlag(scope)) throw new DomainException("Scopes must be distinct CatalogRead or InventoryRead names.");
            result |= scope;
        }
        return result;
    }
    public string[] ScopeNames() => Enum.GetValues<IntegrationKeyScope>().Where(s => Scopes.HasFlag(s)).Select(s => s.ToString()).ToArray();
}
