using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record EditVariantRequest(
    [Required] string Name, [Required] decimal? Price, [Required] int? WeightGrams,
    [Required] [property: JsonConverter(typeof(VariantOptionsJsonConverter))] Dictionary<string, string> Options,
    [Required, Range(0, long.MaxValue)] long? ExpectedVersion);
public sealed record AdminVariantResponse(Guid Id, Guid ProductId, string Sku, string Name, MoneyDto Price,
    int WeightGrams, IReadOnlyDictionary<string, string> Options, long Version)
{
    public static AdminVariantResponse From(ProductVariant variant) => new(variant.Id, variant.ProductId,
        variant.Sku, variant.Name, new MoneyDto(variant.Price.Amount, variant.Price.Currency), variant.WeightGrams, variant.Options, variant.Version);
}
public sealed record AddGalleryImageRequest(
    [Required, MaxLength(2000)] string Url, [MaxLength(500)] string? AltText,
    [Required, Range(0, long.MaxValue)] long? ExpectedVersion);
public sealed record ReorderGalleryRequest([Required] List<Guid> ImageIds, [Required, Range(0, long.MaxValue)] long? ExpectedVersion);
public sealed record GalleryResponse(Guid ProductId, long Version, IReadOnlyList<ImageResponse> Images)
{
    public static GalleryResponse From(Product product) => new(product.Id, product.ImageRevision,
        product.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(ImageResponse.From).ToArray());
}
