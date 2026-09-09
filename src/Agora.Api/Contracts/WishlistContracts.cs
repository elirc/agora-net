using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record CreateWishlistRequest(
    [Required, MaxLength(100)] string Name);

public sealed record AddWishlistItemRequest(
    [Required] Guid ProductVariantId);

public sealed record MoveWishlistItemToCartRequest(
    [Required] string CartToken);

public sealed record EditWishlistNoteRequest(string? Note, [Required, Range(0, long.MaxValue)] long? ExpectedVersion);
public sealed record WishlistNoteResponse(Guid ItemId, string? Note, long NoteVersion);
public sealed record CopyWishlistItemsRequest(
    [Required] Guid SourceId,
    [Required, MinLength(1), MaxLength(50)] List<Guid> ItemIds,
    [Required, Range(0, long.MaxValue)] long? ExpectedTargetVersion) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SourceId == Guid.Empty) yield return new ValidationResult("sourceId must be nonempty.", [nameof(SourceId)]);
        if (ItemIds is not null && (ItemIds.Contains(Guid.Empty) || ItemIds.Distinct().Count() != ItemIds.Count))
            yield return new ValidationResult("itemIds must contain distinct nonempty IDs.", [nameof(ItemIds)]);
    }
}
public sealed record WishlistCopyResponse(IReadOnlyList<Guid> AddedVariantIds, IReadOnlyList<Guid> SkippedVariantIds, long MembershipVersion);

public sealed record WishlistSummaryResponse(
    Guid Id,
    string Name,
    bool IsDefault,
    int ItemCount,
    DateTimeOffset CreatedAt,
    long MembershipVersion = 0)
{
    public static WishlistSummaryResponse From(Wishlist wishlist, int itemCount) => new(
        wishlist.Id,
        wishlist.Name,
        wishlist.IsDefault,
        itemCount,
        wishlist.CreatedAt, wishlist.MembershipVersion);
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
    DateTimeOffset AddedAt,
    string? Note = null,
    long NoteVersion = 0)
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
            item.CreatedAt, item.Note, item.NoteVersion);
    }
}

public sealed record WishlistResponse(
    Guid Id,
    string Name,
    bool IsDefault,
    IReadOnlyList<WishlistItemResponse> Items,
    DateTimeOffset CreatedAt,
    long MembershipVersion = 0)
{
    public int InStockItemCount => Items.Count(i => i.InStock);
    public int OutOfStockItemCount => Items.Count - InStockItemCount;

    public static WishlistResponse From(Wishlist wishlist) => new(
        wishlist.Id,
        wishlist.Name,
        wishlist.IsDefault,
        wishlist.Items.OrderBy(i => i.CreatedAt).Select(WishlistItemResponse.From).ToList(),
        wishlist.CreatedAt, wishlist.MembershipVersion);
}
