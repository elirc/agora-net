using System.ComponentModel.DataAnnotations;
using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record CartItemResponse(
    Guid Id,
    Guid ProductVariantId,
    string Sku,
    string ProductName,
    string VariantName,
    int Quantity,
    MoneyDto UnitPrice,
    MoneyDto LineTotal);

public sealed record CartResponse(
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CartItemResponse> Items,
    int TotalQuantity,
    MoneyDto Subtotal)
{
    /// <summary>Maps a cart whose items have their variants (and products) loaded.</summary>
    public static CartResponse From(Cart cart)
    {
        var items = new List<CartItemResponse>(cart.Items.Count);
        var subtotal = Money.Zero(
            cart.Items.FirstOrDefault()?.ProductVariant?.Price.Currency ?? Money.DefaultCurrency);

        foreach (var item in cart.Items)
        {
            var variant = item.ProductVariant
                ?? throw new InvalidOperationException("Cart item variant not loaded.");
            var lineTotal = variant.Price.Multiply(item.Quantity);
            subtotal = subtotal.Add(lineTotal);
            items.Add(new CartItemResponse(
                item.Id,
                item.ProductVariantId,
                variant.Sku,
                variant.Product?.Name ?? string.Empty,
                variant.Name,
                item.Quantity,
                new MoneyDto(variant.Price.Amount, variant.Price.Currency),
                new MoneyDto(lineTotal.Amount, lineTotal.Currency)));
        }

        return new CartResponse(
            cart.Token,
            cart.CreatedAt,
            cart.UpdatedAt,
            items,
            items.Sum(i => i.Quantity),
            new MoneyDto(subtotal.Amount, subtotal.Currency));
    }
}

public sealed record AddCartItemRequest(
    [Required] Guid ProductVariantId,
    [Range(1, Cart.MaxQuantityPerLine)] int Quantity);

public sealed record UpdateCartItemRequest(
    [Range(0, Cart.MaxQuantityPerLine)] int Quantity);
