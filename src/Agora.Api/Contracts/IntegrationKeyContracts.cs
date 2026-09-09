using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record CreateIntegrationKeyRequest([Required, MaxLength(80)] string Name,
    [Range(1, 90)] int ExpiryDays, [Required, MinLength(1), MaxLength(2)] List<string> Scopes);
public sealed record IntegrationKeyResponse(Guid Id, string Name, IReadOnlyList<string> Scopes,
    DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt)
{
    public static IntegrationKeyResponse From(IntegrationApiKey key) => new(key.Id, key.Name, key.ScopeNames(), key.ExpiresAt, key.RevokedAt);
}
public sealed record IntegrationKeyCreatedResponse(IntegrationKeyResponse Key, string ApiKey);
public sealed record IntegrationCatalogRow(Guid ProductId, Guid VariantId, Guid CategoryId, string ProductName,
    string ProductSlug, string Sku, string VariantName, decimal BaseUnitAmount, string Currency, int WeightGrams);
public sealed record IntegrationInventoryRow(Guid VariantId, string Sku, int QuantityOnHand, int QuantityReserved,
    int QuantityAvailable, int Version);
