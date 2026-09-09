using System.ComponentModel.DataAnnotations;

namespace Agora.Api.Contracts;

/// <summary>HTTP input rules; SQL composition lives in ProductCatalogQuery.</summary>
public sealed class ProductSearchRequest : IValidatableObject
{
    public const int MaxPageSize = 100;

    [MaxLength(200)]
    public string? Search { get; init; }
    public Guid? CategoryId { get; init; }
    public string? CategorySlug { get; init; }
    private string? _tagSlug;
    [MaxLength(60), RegularExpression(Agora.Domain.Common.CatalogText.SlugPattern)]
    public string? TagSlug { get => _tagSlug; init => _tagSlug = value?.Trim().ToLowerInvariant(); }
    [Range(typeof(decimal), "0", "1000000")]
    public decimal? MinPrice { get; init; }
    [Range(typeof(decimal), "0", "1000000")]
    public decimal? MaxPrice { get; init; }
    public bool? IsActive { get; init; }
    public bool? InStock { get; init; }
    [MaxLength(64)]
    public string? Sku { get; init; }
    public bool? HasImages { get; init; }

    [RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must be a 3-letter code.")]
    public string? Currency { get; init; }

    public string? Sort { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, MaxPageSize)]
    public int PageSize { get; init; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Query parameters also pass through the cents converter. Reject extra
        // precision rather than silently rounding a boundary to a different price.
        if (MinPrice is { } min && decimal.Round(min, 2) != min)
            yield return new ValidationResult("minPrice must have at most two decimal places.", [nameof(MinPrice)]);
        if (MaxPrice is { } max && decimal.Round(max, 2) != max)
            yield return new ValidationResult("maxPrice must have at most two decimal places.", [nameof(MaxPrice)]);
        if (MinPrice > MaxPrice)
            yield return new ValidationResult("minPrice must not exceed maxPrice.", [nameof(MinPrice), nameof(MaxPrice)]);

        // Widen before multiplying: valid individual integers can overflow together.
        if (Page >= 1 && PageSize >= 1 && (long)(Page - 1) * PageSize > int.MaxValue)
            yield return new ValidationResult("The requested page offset is too large.", [nameof(Page)]);
    }
}
