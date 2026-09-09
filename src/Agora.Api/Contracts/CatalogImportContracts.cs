using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Agora.Api.Contracts;

public sealed record CatalogImportRowRequest([Required, MaxLength(80)] string RowKey, [Required] CreateProductRequest Product);
public sealed record PreviewCatalogImportRequest([Range(1, 1)] int Version,
    [Required, MinLength(1), MaxLength(100)] List<CatalogImportRowRequest> Products) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Products is not null && Products.Any(p => p is null || p.Product is null))
            yield return new("Rows and product objects cannot be null.", [nameof(Products)]);
        if (Products is not null && Products.Where(p => p?.Product?.Variants is not null).Sum(p => (long)p.Product.Variants.Count) > 300)
            yield return new("An import may contain at most 300 variants.", [nameof(Products)]);
    }
}
public sealed record CommitCatalogImportRequest([property: JsonRequired] [Range(0, long.MaxValue)] long Revision,
    [Required, RegularExpression("^[0-9a-f]{64}$")] string Digest);
