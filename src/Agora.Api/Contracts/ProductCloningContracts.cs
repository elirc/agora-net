using System.ComponentModel.DataAnnotations;

namespace Agora.Api.Contracts;

public sealed record CloneVariantSkuRequest(Guid SourceVariantId, [Required, MaxLength(ProductInputRules.SkuLength)] string Sku);
public sealed record CloneProductRequest(
    [Required, MaxLength(ProductInputRules.NameLength)] string Name,
    [Required, MaxLength(ProductInputRules.SlugLength)] string Slug,
    [Required, MaxLength(50)] List<CloneVariantSkuRequest> VariantSkus);
public sealed record ClonedProductResponse(Guid Id, string Slug, bool IsActive);
