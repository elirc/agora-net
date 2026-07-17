using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record CreateWishlistRequest(
    [Required, MaxLength(100)] string Name);

public sealed record AddWishlistItemRequest(
    [Required] Guid ProductVariantId);

public sealed record MoveWishlistItemToCartRequest(
    [Required] string CartToken);

public sealed record WishlistSummaryResponse(
    Guid Id,
    string Name,
    bool IsDefault,
    int ItemCount,
    DateTimeOffset CreatedAt)
{
    public static WishlistSummaryResponse From(Wishlist wishlist, int itemCount) => new(
        wishlist.Id,
        wishlist.Name,
        wishlist.IsDefault,
        itemCount,
        wishlist.CreatedAt);
}

public sealed record WishlistItemResponse(
    Guid Id,
    Guid ProductVariantId,
    string Sku,
    string ProductName,
    string VariantName,
    MoneyDto Price,
    bool InStock,
    bool BackInStock,
    DateTimeOffset AddedAt)
{
    /// <summary>Maps an item whose variant (with product + inventory) is loaded.</summary>
    public static WishlistItemResponse From(WishlistItem item)
    {
        var variant = item.ProductVariant
            ?? throw new InvalidOperationException("Wishlist item variant not loaded.");
        var inStock = (variant.Inventory?.QuantityAvailable ?? 0) > 0;
        return new WishlistItemResponse(
            item.Id,
            item.ProductVariantId,
            variant.Sku,
            variant.Product?.Name ?? string.Empty,
            variant.Name,
            new MoneyDto(variant.Price.Amount, variant.Price.Currency),
            inStock,
            BackInStock: item.OutOfStockObserved && inStock,
            item.CreatedAt);
    }
}

public sealed record WishlistResponse(
    Guid Id,
    string Name,
    bool IsDefault,
    IReadOnlyList<WishlistItemResponse> Items,
    DateTimeOffset CreatedAt)
{
    public static WishlistResponse From(Wishlist wishlist) => new(
        wishlist.Id,
        wishlist.Name,
        wishlist.IsDefault,
        wishlist.Items.OrderBy(i => i.CreatedAt).Select(WishlistItemResponse.From).ToList(),
        wishlist.CreatedAt);
}
