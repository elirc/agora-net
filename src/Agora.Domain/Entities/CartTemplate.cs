using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public sealed record CartTemplateSnapshot(Guid VariantId, int Quantity, string Sku, string ProductName, string VariantName);

public class CartTemplate
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CustomerId { get; private set; }
    public string Name { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public List<CartTemplateLine> Lines { get; private set; } = [];
    private CartTemplate() { }

    public CartTemplate(Guid owner, string name, IReadOnlyList<CartTemplateSnapshot> lines, DateTimeOffset now)
    {
        if (owner == Guid.Empty) throw new DomainException("An owner is required.");
        if (lines.Count is < 1 or > 50 || lines.Select(l => l.VariantId).Distinct().Count() != lines.Count
            || lines.Any(l => l.VariantId == Guid.Empty || l.Quantity is < 1 or > Cart.MaxQuantityPerLine))
            throw new DomainException("A template requires 1 to 50 distinct lines with quantities between 1 and 99.");
        CustomerId = owner; Name = CatalogText.Name(name, 80); CreatedAt = now;
        Lines = lines.Select(l => new CartTemplateLine(Id, l)).ToList();
    }
}

public class CartTemplateLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CartTemplateId { get; private set; }
    // Historical identity: deliberately no variant navigation or foreign key.
    public Guid VariantId { get; private set; }
    public int Quantity { get; private set; }
    public string Sku { get; private set; } = "";
    public string ProductName { get; private set; } = "";
    public string VariantName { get; private set; } = "";
    private CartTemplateLine() { }
    internal CartTemplateLine(Guid templateId, CartTemplateSnapshot source)
    {
        CartTemplateId = templateId; VariantId = source.VariantId; Quantity = source.Quantity;
        Sku = source.Sku; ProductName = source.ProductName; VariantName = source.VariantName;
    }
}
