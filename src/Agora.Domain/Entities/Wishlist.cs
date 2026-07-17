using Agora.Domain.Common;

namespace Agora.Domain.Entities;

/// <summary>
/// A customer's wishlist. Every customer gets a default list on first use and
/// may create additional named lists; names are unique per customer.
/// </summary>
public class Wishlist
{
    public const string DefaultName = "Favorites";

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<WishlistItem> Items { get; set; } = [];

    public WishlistItem AddItem(Guid productVariantId, bool outOfStockNow)
    {
        if (Items.Any(i => i.ProductVariantId == productVariantId))
        {
            throw new DomainException("This item is already on the wishlist.");
        }

        var item = new WishlistItem
        {
            WishlistId = Id,
            ProductVariantId = productVariantId,
            OutOfStockObserved = outOfStockNow,
        };
        Items.Add(item);
        return item;
    }
}

/// <summary>
/// A wishlist entry. <see cref="OutOfStockObserved"/> is set whenever the item
/// is seen out of stock so the API can flag it as "back in stock" once
/// availability returns.
/// </summary>
public class WishlistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WishlistId { get; set; }
    public Wishlist? Wishlist { get; set; }
    public Guid ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    /// <summary>True once the item has ever been observed out of stock.</summary>
    public bool OutOfStockObserved { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
