using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record VariantResponse(
    Guid Id,
    string Sku,
    string Name,
    MoneyDto Price,
    Dictionary<string, string> Options)
{
    public static VariantResponse From(ProductVariant variant) => new(
        variant.Id,
        variant.Sku,
        variant.Name,
        new MoneyDto(variant.Price.Amount, variant.Price.Currency),
        variant.Options);
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
    int ReviewCount)
{
    public static ProductResponse From(
        Product product, decimal? averageRating = null, int reviewCount = 0) => new(
        product.Id,
        product.CategoryId,
        product.Name,
        product.Slug,
        product.Description,
        product.IsActive,
        product.CreatedAt,
        product.Variants.Select(VariantResponse.From).ToList(),
        product.Images.OrderBy(i => i.SortOrder).Select(ImageResponse.From).ToList(),
        averageRating,
        reviewCount);
}

public sealed record CreateVariantRequest(
    [Required, MaxLength(64)] string Sku,
    [MaxLength(200)] string? Name,
    [Range(0, 1_000_000)] decimal Price,
    [RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO code.")]
    string? Currency,
    Dictionary<string, string>? Options);

public sealed record CreateImageRequest(
    [Required, MaxLength(2000), Url] string Url,
    [MaxLength(500)] string? AltText,
    int SortOrder);

public sealed record CreateProductRequest(
    [Required] Guid CategoryId,
    [Required, MaxLength(200)] string Name,
    [MaxLength(200)] string? Slug,
    [MaxLength(4000)] string? Description,
    bool? IsActive,
    [Required, MinLength(1)] List<CreateVariantRequest> Variants,
    List<CreateImageRequest>? Images);

public sealed record UpdateProductRequest(
    [Required] Guid CategoryId,
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(200)] string Slug,
    [MaxLength(4000)] string? Description,
    bool IsActive);
