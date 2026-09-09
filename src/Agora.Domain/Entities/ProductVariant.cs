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

    /// <summary>Unit shipping weight in grams; used by weight-based shipping rates.</summary>
    public int WeightGrams { get; set; }

    /// <summary>Option name -> value, e.g. { "Color": "Red", "Size": "M" }. Stored as JSON.</summary>
    public Dictionary<string, string> Options { get; set; } = [];

    public InventoryItem? Inventory { get; set; }
    public long Version { get; private set; }

    public void Edit(string name, decimal price, int weightGrams, IReadOnlyDictionary<string, string> options)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length is < 1 or > 120) throw new DomainException("Variant name must contain 1–120 characters after trimming.");
        if (price is < 0 or > 1_000_000 || decimal.Round(price, 2) != price)
            throw new DomainException("Price must be between zero and 1,000,000 with at most two decimal places.");
        if (weightGrams is < 0 or > 1_000_000) throw new DomainException("Weight must be between zero and 1,000,000 grams.");
        var normalizedOptions = VariantOptionRules.Normalize(options);
        Name = normalizedName;
        Price = new Money(price, Price.Currency);
        WeightGrams = weightGrams;
        Options = normalizedOptions;
        Version = checked(Version + 1);
    }
}
