using Agora.Domain.Common;

namespace Agora.Domain.Entities;

/// <summary>
/// Guest shopping cart addressed by an opaque token. Line quantities must stay
/// within 1..<see cref="MaxQuantityPerLine"/>; adding an existing variant merges
/// into the existing line.
/// </summary>
public class Cart
{
    public const int MaxQuantityPerLine = 99;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Token { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Owning account, if the cart was created or claimed while signed in.</summary>
    public Guid? CustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<CartItem> Items { get; set; } = [];

    public CartItem AddItem(Guid productVariantId, int quantity)
    {
        ValidateQuantity(quantity);

        // A saved-for-later line for the same variant is re-activated and merged.
        var existing = Items.FirstOrDefault(i => i.ProductVariantId == productVariantId);
        if (existing is null)
        {
            existing = new CartItem
            {
                CartId = Id,
                ProductVariantId = productVariantId,
                Quantity = quantity,
            };
            Items.Add(existing);
        }
        else
        {
            var merged = existing.Quantity + quantity;
            ValidateQuantity(merged);
            existing.Quantity = merged;
            existing.IsSavedForLater = false;
        }

        Touch();
        return existing;
    }

    /// <summary>Parks a line: it stays on the cart but leaves totals and checkout.</summary>
    public CartItem SaveForLater(Guid cartItemId)
    {
        var item = FindItem(cartItemId);
        item.IsSavedForLater = true;
        Touch();
        return item;
    }

    /// <summary>Returns a saved-for-later line to the active cart.</summary>
    public CartItem ActivateItem(Guid cartItemId)
    {
        var item = FindItem(cartItemId);
        item.IsSavedForLater = false;
        Touch();
        return item;
    }

    /// <summary>Active (purchasable) lines; excludes saved-for-later.</summary>
    public IEnumerable<CartItem> ActiveItems => Items.Where(i => !i.IsSavedForLater);

    /// <summary>Removes purchased lines after checkout, keeping saved-for-later ones.</summary>
    public void RemoveActiveItems()
    {
        Items.RemoveAll(i => !i.IsSavedForLater);
        Touch();
    }

    /// <summary>Sets a line's quantity; a quantity of zero removes the line.</summary>
    public void UpdateItemQuantity(Guid cartItemId, int quantity)
    {
        var item = FindItem(cartItemId);
        if (quantity == 0)
        {
            Items.Remove(item);
        }
        else
        {
            ValidateQuantity(quantity);
            item.Quantity = quantity;
        }

        Touch();
    }

    public void RemoveItem(Guid cartItemId)
    {
        Items.Remove(FindItem(cartItemId));
        Touch();
    }

    public void Clear()
    {
        Items.Clear();
        Touch();
    }

    private CartItem FindItem(Guid cartItemId) =>
        Items.FirstOrDefault(i => i.Id == cartItemId)
        ?? throw new NotFoundException($"Cart item '{cartItemId}' not found.");

    private static void ValidateQuantity(int quantity)
    {
        if (quantity is < 1 or > MaxQuantityPerLine)
        {
            throw new DomainException(
                $"Line quantity must be between 1 and {MaxQuantityPerLine}.");
        }
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
