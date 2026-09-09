using System.ComponentModel.DataAnnotations;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;

namespace Agora.Api.Contracts;

public sealed record CartItemResponse(
    Guid Id,
    Guid ProductVariantId,
    string Sku,
    string ProductName,
    string VariantName,
    int Quantity,
    MoneyDto UnitPrice,
    MoneyDto LineTotal)
{
    public MoneyDto BaseUnitPrice { get; init; } = UnitPrice;
    public int? SelectedMinimumQuantity { get; init; }
}

public sealed record CartResponse(
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CartItemResponse> Items,
    IReadOnlyList<CartItemResponse> SavedItems,
    int TotalQuantity,
    MoneyDto Subtotal,
    int Version = 0)
{
    public int ActiveLineCount => Items.Count;
    public int SavedLineCount => SavedItems.Count;

    /// <summary>
    /// Maps a cart whose items have their variants (and products) loaded.
    /// Saved-for-later lines are listed separately and excluded from totals.
    /// </summary>
    public static CartResponse From(Cart cart, IReadOnlyDictionary<Guid, CalculatedVariantPrice>? prices = null)
    {
        var items = new List<CartItemResponse>(cart.Items.Count);
        var savedItems = new List<CartItemResponse>();
        var subtotal = Money.Zero(
            cart.ActiveItems.FirstOrDefault()?.ProductVariant?.Price.Currency ?? Money.DefaultCurrency);

        foreach (var item in cart.Items)
        {
            var variant = item.ProductVariant
                ?? throw new InvalidOperationException("Cart item variant not loaded.");
            var selected = prices is null ? new CalculatedVariantPrice(variant.Price, variant.Price, null) : prices[item.Id];
            var lineTotal = selected.AppliedPrice.Multiply(item.Quantity);
            var response = new CartItemResponse(
                item.Id,
                item.ProductVariantId,
                variant.Sku,
                variant.Product?.Name ?? string.Empty,
                variant.Name,
                item.Quantity,
                new MoneyDto(selected.AppliedPrice.Amount, selected.AppliedPrice.Currency),
                new MoneyDto(lineTotal.Amount, lineTotal.Currency))
                {
                    BaseUnitPrice = new MoneyDto(selected.BasePrice.Amount, selected.BasePrice.Currency),
                    SelectedMinimumQuantity = selected.MinimumQuantity,
                };

            if (item.IsSavedForLater)
            {
                savedItems.Add(response);
            }
            else
            {
                subtotal = subtotal.Add(lineTotal);
                items.Add(response);
            }
        }

        return new CartResponse(
            cart.Token,
            cart.CreatedAt,
            cart.UpdatedAt,
            items,
            savedItems,
            items.Sum(i => i.Quantity),
            new MoneyDto(subtotal.Amount, subtotal.Currency),
            cart.Version);
    }
}

public sealed record AddCartItemRequest(
    [Required] Guid ProductVariantId,
    [Range(1, Cart.MaxQuantityPerLine)] int Quantity);

public sealed record UpdateCartItemRequest(
    [Range(0, Cart.MaxQuantityPerLine)] int Quantity);
