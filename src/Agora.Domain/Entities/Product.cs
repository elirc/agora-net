namespace Agora.Domain.Entities;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Tax classification; null products use each tax zone's default rate.</summary>
    public Guid? TaxCategoryId { get; set; }
    public TaxCategory? TaxCategory { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProductVariant> Variants { get; set; } = [];
    public List<ProductImage> Images { get; set; } = [];
    public List<ProductTag> Tags { get; set; } = [];
    public long TagVersion { get; private set; }
    public long ImageRevision { get; private set; }
    public long CatalogRevision { get; private set; }

    public void AdvanceCatalogRevision()
    {
        CatalogRevision = checked(CatalogRevision + 1);
    }

    public ProductImage AddGalleryImage(string url, string? altText)
    {
        var normalized = url.Trim();
        if (normalized.Length > 2000 || !Uri.TryCreate(normalized, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            throw new Agora.Domain.Common.DomainException("Image URL must be an absolute HTTP or HTTPS link of at most 2,000 characters.");
        if (altText?.Length > 500) throw new Agora.Domain.Common.DomainException("Alt text may contain at most 500 characters.");
        if (Images.Count >= 10) throw new Agora.Domain.Common.DomainException("A gallery with ten or more images cannot accept another image.");
        CompactImagePositions();
        var image = new ProductImage { ProductId = Id, Url = normalized, AltText = altText, SortOrder = Images.Count };
        Images.Add(image);
        ImageRevision = checked(ImageRevision + 1);
        return image;
    }

    public void ReplaceImageOrder(IReadOnlyList<Guid> imageIds)
    {
        if (imageIds.Distinct().Count() != imageIds.Count || !Images.Select(i => i.Id).ToHashSet().SetEquals(imageIds))
            throw new Agora.Domain.Common.DomainException("Supply an exact permutation of the gallery image IDs.");
        var byId = Images.ToDictionary(i => i.Id);
        for (var position = 0; position < imageIds.Count; position++) byId[imageIds[position]].SortOrder = position;
        ImageRevision = checked(ImageRevision + 1);
    }

    public void RemoveGalleryImage(Guid imageId)
    {
        var image = Images.SingleOrDefault(i => i.Id == imageId)
            ?? throw new Agora.Domain.Common.DomainException("The image is not in this gallery.");
        Images.Remove(image);
        CompactImagePositions();
        ImageRevision = checked(ImageRevision + 1);
    }

    private void CompactImagePositions()
    {
        var ordered = Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
        for (var position = 0; position < ordered.Count; position++) ordered[position].SortOrder = position;
    }

    public void ReplaceTags(IReadOnlyList<Guid> tagIds)
    {
        if (tagIds.Count > 20 || tagIds.Contains(Guid.Empty) || tagIds.Distinct().Count() != tagIds.Count)
            throw new Agora.Domain.Common.DomainException("A product must have at most 20 distinct, nonempty tag IDs.");
        var wanted = tagIds.ToHashSet();
        Tags.RemoveAll(t => !wanted.Contains(t.TagId));
        var existing = Tags.Select(t => t.TagId).ToHashSet();
        foreach (var id in tagIds)
            if (existing.Add(id)) Tags.Add(new ProductTag { ProductId = Id, TagId = id });
        TagVersion = checked(TagVersion + 1);
    }
}
