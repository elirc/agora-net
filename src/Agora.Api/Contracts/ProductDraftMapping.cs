using Agora.Domain.Common;
using Agora.Infrastructure.Services;

namespace Agora.Api.Contracts;

public static class ProductDraftMapping
{
    public static ProductDraftInput ToDraft(this CreateProductRequest request, bool forceInactive = false) => new(
        request.CategoryId, request.Name.Trim(),
        string.IsNullOrWhiteSpace(request.Slug) ? SlugGenerator.FromName(request.Name) : request.Slug.Trim(),
        request.Description ?? string.Empty, !forceInactive && (request.IsActive ?? true),
        request.Variants.Select(v => new ProductDraftVariant(v.Sku.Trim(), v.Name?.Trim() ?? string.Empty,
            v.Price, (v.Currency ?? Money.DefaultCurrency).ToUpperInvariant(),
            (v.Options ?? []).OrderBy(o => o.Key, StringComparer.Ordinal).ToDictionary(o => o.Key, o => o.Value), v.WeightGrams)).ToArray(),
        (request.Images ?? []).Select(i => new ProductDraftImage(i.Url, i.AltText, i.SortOrder)).ToArray(),
        string.IsNullOrWhiteSpace(request.TaxCategoryCode) ? null : request.TaxCategoryCode.Trim().ToLowerInvariant());
}
