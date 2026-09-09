using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record VariantResponse(
    Guid Id,
    string Sku,
    string Name,
    MoneyDto Price,
    Dictionary<string, string> Options,
    int WeightGrams = 0)
{
    public static VariantResponse From(ProductVariant variant) => new(
        variant.Id,
        variant.Sku,
        variant.Name,
        new MoneyDto(variant.Price.Amount, variant.Price.Currency),
        variant.Options,
        variant.WeightGrams);
}

public sealed record ImageResponse(Guid Id, string Url, string? AltText, int SortOrder)
{
    public static ImageResponse From(ProductImage image) =>
        new(image.Id, image.Url, image.AltText, image.SortOrder);
}

public sealed record ProductResponse(
    Guid Id,
    Guid CategoryId,
    string Name,
    string Slug,
    string Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<VariantResponse> Variants,
    IReadOnlyList<ImageResponse> Images,
    decimal? AverageRating,
    int ReviewCount,
    string? TaxCategoryCode)
{
    public int VariantCount => Variants.Count;
    public ImageResponse? PrimaryImage => Images.FirstOrDefault();
    public IReadOnlyList<TagResponse> Tags { get; init; } = [];
    public long TagVersion { get; init; }

    public static ProductResponse From(
        Product product, decimal? averageRating = null, int reviewCount = 0) => new(
        product.Id,
        product.CategoryId,
        product.Name,
        product.Slug,
        product.Description,
        product.IsActive,
        product.CreatedAt,
        product.Variants.OrderBy(v => v.Sku, StringComparer.Ordinal).ThenBy(v => v.Id)
            .Select(VariantResponse.From).ToList(),
        product.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(ImageResponse.From).ToList(),
        averageRating,
        reviewCount,
        product.TaxCategory?.Code)
        {
            Tags = product.Tags.OrderBy(t => t.Tag!.Slug, StringComparer.Ordinal).Select(t => TagResponse.From(t.Tag!)).ToArray(),
            TagVersion = product.TagVersion,
        };
}

public sealed record CreateVariantRequest(
    [Required, MaxLength(ProductInputRules.SkuLength)] string Sku,
    [MaxLength(200)] string? Name,
    [Range(0, 1_000_000)] decimal Price,
    [RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO code.")]
    string? Currency,
    Dictionary<string, string>? Options,
    [Range(0, 1_000_000)] int WeightGrams = 0);

public sealed record CreateImageRequest(
    [Required, MaxLength(2000), Url] string Url,
    [MaxLength(500)] string? AltText,
    int SortOrder);

public sealed record CreateProductRequest(
    [Required] Guid CategoryId,
    [Required, MaxLength(ProductInputRules.NameLength)] string Name,
    [MaxLength(ProductInputRules.SlugLength)] string? Slug,
    [MaxLength(4000)] string? Description,
    bool? IsActive,
    [Required, MinLength(1)] List<CreateVariantRequest> Variants,
    [MaxLength(10)] List<CreateImageRequest>? Images,
    [MaxLength(64)] string? TaxCategoryCode = null);

public sealed record UpdateProductRequest(
    [Required] Guid CategoryId,
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(200)] string Slug,
    [MaxLength(4000)] string? Description,
    bool IsActive,
    [MaxLength(64)] string? TaxCategoryCode = null);
