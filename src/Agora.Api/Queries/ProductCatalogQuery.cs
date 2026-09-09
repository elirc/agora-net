using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Queries;

/// <summary>Composes SQL expressions without executing them or owning a DbContext.</summary>
internal static class ProductCatalogQuery
{
    public static IOrderedQueryable<Product> Apply(IQueryable<Product> query, ProductSearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Search is literal text: LIKE metacharacters must not broaden a query.
            var escaped = request.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var term = $"%{escaped}%";
            query = query.Where(p => EF.Functions.Like(p.Name, term, "\\")
                || EF.Functions.Like(p.Description, term, "\\"));
        }

        if (request.CategoryId is { } categoryId)
            query = query.Where(p => p.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(request.CategorySlug))
            query = query.Where(p => p.Category!.Slug == request.CategorySlug);
        if (!string.IsNullOrWhiteSpace(request.TagSlug))
            query = query.Where(p => p.Tags.Any(t => t.Tag!.Slug == request.TagSlug));
        if (request.IsActive is { } active)
            query = query.Where(p => p.IsActive == active);
        if (request.HasImages is { } hasImages)
            query = query.Where(p => p.Images.Any() == hasImages);

        var currency = string.IsNullOrEmpty(request.Currency) ? null : request.Currency.ToUpperInvariant();
        var sku = string.IsNullOrWhiteSpace(request.Sku) ? null : request.Sku.Trim();
        var min = request.MinPrice;
        var max = request.MaxPrice;
        var inStock = request.InStock;
        if (min.HasValue || max.HasValue || currency is not null || inStock.HasValue || sku is not null)
        {
            // One variant must satisfy ALL variant filters. Separate Any calls
            // could match a cheap sold-out variant and an expensive available one.
            query = query.Where(p => p.Variants.Any(v =>
                (!min.HasValue || v.Price.Amount >= min.Value)
                && (!max.HasValue || v.Price.Amount <= max.Value)
                && (currency == null || v.Price.Currency == currency)
                && (sku == null || v.Sku == sku)
                && (!inStock.HasValue
                    || (inStock.Value
                        ? v.Inventory != null && v.Inventory.QuantityOnHand - v.Inventory.QuantityReserved > 0
                        : v.Inventory == null || v.Inventory.QuantityOnHand - v.Inventory.QuantityReserved <= 0))));
        }

        // Preserve the existing price-sort contract: cheapest variant overall,
        // even when another variant caused the product to match the filters.
        var ordered = request.Sort?.ToLowerInvariant() switch
        {
            "name" => query.OrderBy(p => p.Name),
            "name_desc" => query.OrderByDescending(p => p.Name),
            "price" => query.OrderBy(p => p.Variants.Min(v => v.Price.Amount)),
            "price_desc" => query.OrderByDescending(p => p.Variants.Min(v => v.Price.Amount)),
            "oldest" => query.OrderBy(p => p.CreatedAt),
            _ => query.OrderByDescending(p => p.CreatedAt),
        };

        // A unique tie-breaker makes page boundaries deterministic on unchanged data.
        return ordered.ThenBy(p => p.Id);
    }
}
