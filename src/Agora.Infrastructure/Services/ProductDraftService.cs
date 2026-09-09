using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record ProductDraftInput(Guid CategoryId, string Name, string Slug, string Description,
    bool IsActive, IReadOnlyList<ProductDraftVariant> Variants, IReadOnlyList<ProductDraftImage> Images,
    string? TaxCategoryCode);
public sealed record ProductDraftVariant(string Sku, string Name, decimal Price, string Currency,
    Dictionary<string, string> Options, int WeightGrams);
public sealed record ProductDraftImage(string Url, string? AltText, int SortOrder);
public sealed record ProductDraftError(string Field, string Code, string Message, int Status = 422);
public sealed record ValidatedProductDraft(Product? Product, IReadOnlyList<ProductDraftError> Errors);

/// <summary>Shared create validation and graph construction. The caller owns the transaction and save.</summary>
public sealed class ProductDraftService(AgoraDbContext db, CategoryOptionSchemaService schemas)
{
    public async Task<ValidatedProductDraft> ValidateAndBuildAsync(ProductDraftInput input, CancellationToken ct)
    {
        var errors = new List<ProductDraftError>();
        if (!await db.Categories.AnyAsync(c => c.Id == input.CategoryId, ct))
            errors.Add(new("categoryId", "MissingCategory", "Category does not exist."));
        if (await db.Products.AnyAsync(p => p.Slug == input.Slug, ct))
            errors.Add(new("slug", "SlugExists", $"A product with slug '{input.Slug}' already exists.", 409));
        var skus = input.Variants.Select(v => v.Sku).ToArray();
        if (skus.Distinct(StringComparer.OrdinalIgnoreCase).Count() != skus.Length)
            errors.Add(new("variants", "DuplicateSku", "Duplicate SKUs in request."));
        if (await db.ProductVariants.AnyAsync(v => skus.Contains(v.Sku), ct))
            errors.Add(new("variants", "SkuExists", "One or more SKUs already exist.", 409));
        Guid? taxId = null;
        if (input.TaxCategoryCode is not null)
        {
            taxId = await db.TaxCategories.Where(t => t.Code == input.TaxCategoryCode).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(ct);
            if (taxId is null) errors.Add(new("taxCategoryCode", "MissingTaxCategory", $"Tax category '{input.TaxCategoryCode}' does not exist."));
        }
        if (errors.Count != 0) return new(null, errors);
        var product = new Product { CategoryId = input.CategoryId, Name = input.Name, Slug = input.Slug,
            Description = input.Description, IsActive = input.IsActive, TaxCategoryId = taxId };
        foreach (var source in input.Variants)
        {
            var variant = new ProductVariant { ProductId = product.Id, Sku = source.Sku, Name = source.Name,
                Price = new Money(source.Price, source.Currency), Options = new(source.Options), WeightGrams = source.WeightGrams };
            variant.Inventory = new InventoryItem(variant.Id, 0);
            product.Variants.Add(variant);
        }
        foreach (var source in input.Images)
            product.Images.Add(new ProductImage { ProductId = product.Id, Url = source.Url, AltText = source.AltText, SortOrder = source.SortOrder });
        // Keep the rich schema exception intact for single-product callers; imports translate it per row.
        await schemas.ValidateAuthoringAsync(product.CategoryId,
            product.Variants.Select(v => new VariantOptionCandidate(v.Id, v.Sku, v.Options)).ToArray(), ct);
        return new(product, []);
    }
}
