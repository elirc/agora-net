using System.ComponentModel.DataAnnotations;

namespace Agora.Api.Contracts;

public sealed record ProductComparisonRequest(
    [Required, MinLength(2), MaxLength(4)] List<Guid> ProductIds) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProductIds is not null && (ProductIds.Contains(Guid.Empty) || ProductIds.Distinct().Count() != ProductIds.Count))
            yield return new ValidationResult("productIds must contain distinct, nonempty IDs.", [nameof(ProductIds)]);
    }
}

public sealed record ComparisonVariantResponse(
    Guid Id, string Sku, string Name, MoneyDto Price, int WeightGrams,
    IReadOnlyDictionary<string, string> Options, bool InStock);

public sealed record ComparedProductResponse(
    Guid Id, string Name, string Slug, CategoryResponse Category,
    IReadOnlyList<ImageResponse> Images, decimal? AverageRating, int ReviewCount,
    IReadOnlyList<ComparisonVariantResponse> Variants);

public sealed record ProductComparisonResponse(IReadOnlyList<ComparedProductResponse> Products);
public sealed record RatingBucketResponse(int Stars, long Count);
public sealed record ReviewSummaryResponse(long TotalCount, decimal? AverageRating, IReadOnlyList<RatingBucketResponse> Buckets);
