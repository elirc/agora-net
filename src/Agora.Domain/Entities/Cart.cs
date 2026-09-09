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

    /// <summary>Optimistic-concurrency token; bumped by every cart mutation.</summary>
    public int Version { get; private set; }

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

    public void Claim(Guid customerId, DateTimeOffset now)
    {
        if (customerId == Guid.Empty) throw new DomainException("A customer is required to claim a cart.");
        if (CustomerId is { } owner && owner != customerId) throw new DomainException("Cart belongs to another customer.");
        if (CustomerId == customerId) return;
        MembershipChanged(now);
        CustomerId = customerId;
    }

    /// <summary>Advances the parent when a related catalog deletion removes child lines by cascade.</summary>
    public void MembershipChanged(DateTimeOffset now)
    {
        if (Version == int.MaxValue) throw new DomainException("The cart revision cannot advance further.");
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Replaces a fully validated combination, preserving IDs for variants already in this cart.</summary>
    public void ReplaceContents(IReadOnlyList<CartLineState> proposed, DateTimeOffset now)
    {
        if (proposed.Any(l => l.VariantId == Guid.Empty) || proposed.Select(l => l.VariantId).Distinct().Count() != proposed.Count)
            throw new DomainException("Cart contents must use distinct nonempty variant IDs.");
        foreach (var line in proposed) ValidateQuantity(line.Quantity);
        if (Version == int.MaxValue) throw new DomainException("The cart revision cannot advance further.");
        var wanted = proposed.Select(l => l.VariantId).ToHashSet();
        var existing = Items.ToDictionary(i => i.ProductVariantId);
        Items.RemoveAll(i => !wanted.Contains(i.ProductVariantId));
        foreach (var line in proposed)
        {
            if (!existing.TryGetValue(line.VariantId, out var item))
            {
                item = new CartItem { CartId = Id, ProductVariantId = line.VariantId };
                Items.Add(item);
            }
            item.Quantity = line.Quantity;
            item.IsSavedForLater = line.IsSavedForLater;
        }
        UpdatedAt = now;
        Version++;
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

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        Version++;
    }
}

public sealed record CartLineState(Guid VariantId, int Quantity, bool IsSavedForLater);
