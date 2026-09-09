using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class IntegrationApiKeyTests
{
    [Fact]
    public void Expiry_is_exclusive_and_revoke_is_idempotent()
    {
        var now = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        var key = new IntegrationApiKey(Guid.NewGuid(), " Test ", new byte[32], IntegrationKeyScope.CatalogRead, Guid.NewGuid(), now, 1);
        Assert.True(key.IsActive(now.AddDays(1).AddTicks(-1))); Assert.False(key.IsActive(now.AddDays(1)));
        key.Revoke(now.AddMinutes(1)); key.Revoke(now.AddMinutes(2));
        Assert.Equal(now.AddMinutes(1), key.RevokedAt); Assert.False(key.IsActive(now.AddMinutes(1)));
    }
    [Fact]
    public void Scope_names_are_explicit_not_enum_numeric_or_comma_syntax()
    {
        Assert.Equal(IntegrationKeyScope.CatalogRead | IntegrationKeyScope.InventoryRead, IntegrationApiKey.ParseScopes(["catalogread", " InventoryRead "]));
        foreach (var values in new string[][] { [], ["1"], ["Admin"], ["CatalogRead,InventoryRead"], ["CatalogRead", " catalogread "] })
            Assert.Throws<DomainException>(() => IntegrationApiKey.ParseScopes(values));
    }
}
