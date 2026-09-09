using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Domain.Services;

/// <summary>Copies catalog values into a new graph; identity, stock and operational history start fresh.</summary>
public static class ProductDraftCloner
{
    public static Product Clone(Product source, string name, string slug, IReadOnlyDictionary<Guid, string> variantSkus)
    {
        if (source.Variants.Count > 50 || variantSkus.Count != source.Variants.Count || source.Variants.Any(v => !variantSkus.ContainsKey(v.Id)))
            throw new DomainException("A draft clone requires one new SKU for every source variant, with at most 50 variants.");
        var clone = new Product
        {
            Name = name.Trim(), Slug = slug.Trim(), Description = source.Description,
            CategoryId = source.CategoryId, TaxCategoryId = source.TaxCategoryId, IsActive = false,
        };
        foreach (var original in source.Variants)
        {
            var variant = new ProductVariant
            {
                ProductId = clone.Id, Sku = variantSkus[original.Id], Name = original.Name,
                Price = new Money(original.Price.Amount, original.Price.Currency), WeightGrams = original.WeightGrams,
                Options = new Dictionary<string, string>(original.Options),
            };
            variant.Inventory = new InventoryItem(variant.Id, 0);
            clone.Variants.Add(variant);
        }
        // Public galleries break equal sort positions by ID. Assign sorted new IDs
        // in the old visible order so ties stay in the same order without reusing IDs.
        var images = source.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToArray();
        var imageIds = images.Select(_ => Guid.NewGuid()).Order().ToArray();
        for (var index = 0; index < images.Length; index++)
        {
            var original = images[index];
            clone.Images.Add(new ProductImage
            {
                Id = imageIds[index], ProductId = clone.Id, Url = original.Url, AltText = original.AltText, SortOrder = original.SortOrder,
            });
        }
        return clone;
    }
}
