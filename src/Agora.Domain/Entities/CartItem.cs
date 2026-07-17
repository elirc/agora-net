namespace Agora.Domain.Entities;

public class CartItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CartId { get; set; }
    public Cart? Cart { get; set; }
    public Guid ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }
    public int Quantity { get; set; }

    /// <summary>Saved-for-later lines stay in the cart but are excluded from totals and checkout.</summary>
    public bool IsSavedForLater { get; set; }
}
