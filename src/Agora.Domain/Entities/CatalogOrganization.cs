using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public class Tag
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    private Tag() { }
    public Tag(string name, string slug) { Name = CatalogText.Name(name, 60); Slug = CatalogText.Slug(slug); }
}

public class ProductTag
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid TagId { get; set; }
    public Tag? Tag { get; set; }
}

public class ProductCollection
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Title { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public bool IsPublished { get; private set; }
    public long Version { get; private set; }
    public List<CollectionItem> Items { get; set; } = [];
    private ProductCollection() { }
    public ProductCollection(string title, string slug) { Title = CatalogText.Name(title, 120); Slug = CatalogText.Slug(slug); }

    public void Replace(string title, bool published, IReadOnlyList<Guid> productIds)
    {
        var normalizedTitle = CatalogText.Name(title, 120);
        if (productIds.Count > 100 || productIds.Contains(Guid.Empty) || productIds.Distinct().Count() != productIds.Count)
            throw new DomainException("A collection must contain at most 100 distinct, nonempty product IDs.");
        var wanted = productIds.ToHashSet();
        Items.RemoveAll(i => !wanted.Contains(i.ProductId));
        var existing = Items.ToDictionary(i => i.ProductId);
        for (var position = 0; position < productIds.Count; position++)
        {
            var id = productIds[position];
            if (!existing.TryGetValue(id, out var item))
            {
                item = new CollectionItem { CollectionId = Id, ProductId = id };
                Items.Add(item);
            }
            item.Position = position;
        }
        Title = normalizedTitle;
        IsPublished = published;
        MembershipChanged();
    }

    public void MembershipChanged() => Version = checked(Version + 1);
}

public class CollectionItem
{
    public Guid CollectionId { get; set; }
    public ProductCollection? Collection { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public int Position { get; set; }
}
