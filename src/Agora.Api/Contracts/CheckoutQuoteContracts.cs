using System.ComponentModel.DataAnnotations;
using Agora.Infrastructure.Services;

namespace Agora.Api.Contracts;

public sealed record CheckoutQuoteRequest([Required] string CartToken,
    [Required, EmailAddress, MaxLength(320)] string Email, AddressDto? ShippingAddress = null,
    [MaxLength(64)] string? DiscountCode = null, [MaxLength(64)] string? ShippingMethodCode = null,
    Guid? ShippingAddressId = null, [MaxLength(32)] string? GiftCardCode = null, bool UseSavedPreferences = false);

public sealed record CheckoutQuoteLine(Guid CartItemId, Guid VariantId, string Sku, string ProductName, string VariantName,
    int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record CheckoutQuoteResponse(DateTimeOffset CalculatedAt, int CartVersion, string Currency,
    IReadOnlyList<CheckoutQuoteLine> Lines, decimal Subtotal, decimal DiscountAmount, decimal TaxAmount,
    decimal ShippingAmount, decimal Total, decimal GiftCardAmount, decimal RemainingPayable,
    string ShippingMethodCode, long TotalWeightGrams, DateTimeOffset EstimatedDeliveryFrom, DateTimeOffset EstimatedDeliveryTo)
{
    public static CheckoutQuoteResponse From(CheckoutPricingResult result) => new(result.CalculatedAt, result.Cart.Version,
        result.Total.Currency, result.Items.OrderBy(i => i.ProductVariant!.Sku, StringComparer.Ordinal).ThenBy(i => i.Id)
            .Select(i => new CheckoutQuoteLine(i.Id, i.ProductVariantId, i.ProductVariant!.Sku,
                i.ProductVariant.Product?.Name ?? "", i.ProductVariant.Name, i.Quantity,
                result.LinePrices[i.Id].AppliedPrice.Amount, result.LinePrices[i.Id].AppliedPrice.Multiply(i.Quantity).Amount)).ToArray(),
        result.Subtotal.Amount, result.DiscountAmount.Amount, result.TaxAmount.Amount, result.ShippingAmount.Amount,
        result.Total.Amount, result.GiftCardApplied, result.ChargeAmount.Amount, result.ShippingMethod.Code, result.TotalWeightGrams,
        result.EstimatedDeliveryFrom, result.EstimatedDeliveryTo);
}
