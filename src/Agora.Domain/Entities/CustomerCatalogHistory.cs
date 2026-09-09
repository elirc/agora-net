using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public class SavedCatalogSearch
{
    public const int MaximumDefinitionLength = 8192;
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CustomerId { get; private set; }
    public string Name { get; private set; } = "";
    public int SchemaVersion { get; private set; }
    public string DefinitionJson { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    private SavedCatalogSearch() { }
    public SavedCatalogSearch(Guid owner, string name, string definitionJson, DateTimeOffset now)
    {
        if (definitionJson.Length is < 2 or > MaximumDefinitionLength) throw new DomainException("Saved definition must contain at most 8,192 characters.");
        CustomerId = owner; Name = CatalogText.Name(name, 80); SchemaVersion = 1;
        DefinitionJson = definitionJson; CreatedAt = now;
    }
}

public class RecentlyViewedProduct
{
    public Guid CustomerId { get; private set; }
    public Guid ProductId { get; private set; }
    public Product? Product { get; private set; }
    public DateTimeOffset LastViewedAt { get; private set; }
    private RecentlyViewedProduct() { }
    public RecentlyViewedProduct(Guid owner, Guid productId, DateTimeOffset now)
    { CustomerId = owner; ProductId = productId; LastViewedAt = now; }
    public void RecordView(DateTimeOffset now) => LastViewedAt = now;
}
