using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public class ProductVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string Sku { get; set; } = string.Empty;

    /// <summary>Display name for the variant, e.g. "Red / Medium".</summary>
    public string Name { get; set; } = string.Empty;

    public Money Price { get; set; } = Money.Zero();

    /// <summary>Option name -> value, e.g. { "Color": "Red", "Size": "M" }. Stored as JSON.</summary>
    public Dictionary<string, string> Options { get; set; } = [];

    public InventoryItem? Inventory { get; set; }
}
