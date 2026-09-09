using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record TagResponse(Guid Id, string Name, string Slug)
{
    public static TagResponse From(Tag tag) => new(tag.Id, tag.Name, tag.Slug);
}
public sealed record CreateTagRequest([Required] string Name, [Required] string Slug);
public sealed record ReplaceProductTagsRequest(
    [Required, MaxLength(20)] List<Guid> TagIds,
    [Required, Range(0, long.MaxValue)] long? ExpectedVersion) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TagIds is not null && (TagIds.Contains(Guid.Empty) || TagIds.Distinct().Count() != TagIds.Count))
            yield return new ValidationResult("tagIds must contain distinct nonempty IDs.", [nameof(TagIds)]);
    }
}
public sealed record ProductTagsResponse(IReadOnlyList<TagResponse> Tags, long TagVersion);
public sealed record CreateCollectionRequest([Required] string Title, [Required] string Slug);
public sealed record ReplaceCollectionRequest(
    [Required] string Title, bool IsPublished,
    [Required, MaxLength(100)] List<Guid> ProductIds,
    [Required, Range(0, long.MaxValue)] long? ExpectedVersion);
public sealed record CollectionAdminResponse(Guid Id, string Title, string Slug, bool IsPublished, long Version, IReadOnlyList<Guid> ProductIds)
{
    public static CollectionAdminResponse From(ProductCollection collection) => new(collection.Id,
        collection.Title, collection.Slug, collection.IsPublished, collection.Version,
        collection.Items.OrderBy(i => i.Position).ThenBy(i => i.ProductId).Select(i => i.ProductId).ToArray());
}
public sealed record PublicCollectionResponse(Guid Id, string Title, string Slug, PagedResult<ProductResponse> Products);
