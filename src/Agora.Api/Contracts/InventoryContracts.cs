using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record InventoryResponse(
    string Sku,
    Guid ProductVariantId,
    int QuantityOnHand,
    int QuantityReserved,
    int QuantityAvailable)
{
    public static InventoryResponse From(string sku, InventoryItem item) => new(
        sku,
        item.ProductVariantId,
        item.QuantityOnHand,
        item.QuantityReserved,
        item.QuantityAvailable);
}

public sealed record SetStockRequest([Range(0, 1_000_000)] int QuantityOnHand);
