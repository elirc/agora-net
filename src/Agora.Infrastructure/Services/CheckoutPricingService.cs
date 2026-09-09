using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record CheckoutPricingInput(string CartToken, Address? ShippingAddress, string? DiscountCode,
    Guid? CustomerId = null, string? ShippingMethodCode = null, Guid? ShippingAddressId = null, string? GiftCardCode = null,
    bool UseSavedPreferences = false);

public sealed record CheckoutPricingResult(DateTimeOffset CalculatedAt, Cart Cart, IReadOnlyList<CartItem> Items,
    Address ShippingAddress, ShippingMethod ShippingMethod, DiscountCode? Discount, GiftCard? GiftCard,
    Money Subtotal, Money DiscountAmount, Money TaxAmount, Money ShippingAmount, Money Total,
    decimal GiftCardApplied, Money ChargeAmount, long TotalWeightGrams, DateTimeOffset EstimatedDeliveryFrom,
    DateTimeOffset EstimatedDeliveryTo)
{
    public IReadOnlyDictionary<Guid, CalculatedVariantPrice> LinePrices { get; init; } = new Dictionary<Guid, CalculatedVariantPrice>();
}

/// <summary>Loads, validates and calculates. Never persists, reserves, redeems or calls a gateway.</summary>
public class CheckoutPricingService(AgoraDbContext db, TaxService taxService, TimeProvider clock,
    ShippingRulesService shippingRules, VariantLinePricingService variantLinePricing)
{
    public async Task<CheckoutPricingResult> CalculateAsync(CheckoutPricingInput input, bool tracking, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        if (input.UseSavedPreferences)
        {
            if (input.CustomerId is null) throw new DomainException("Sign in to use saved checkout preferences.");
            var preference = await db.CheckoutPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.CustomerId == input.CustomerId, ct);
            // Resolve each dimension separately. Supplied invalid selections are never replaced by a default.
            if (input.ShippingAddress is null && input.ShippingAddressId is null)
                input = input with { ShippingAddressId = preference?.ShippingAddressId };
            if (input.ShippingMethodCode is null)
                input = input with { ShippingMethodCode = preference?.ShippingMethodCode };
            else if (string.IsNullOrWhiteSpace(input.ShippingMethodCode))
                throw new InvalidShippingMethodException("An explicit shipping selection cannot be empty.");
        }
        var query = db.Carts.Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v!.Product)
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v!.Inventory).AsSplitQuery();
        if (!tracking) query = query.AsNoTracking();
        var cart = await query.FirstOrDefaultAsync(c => c.Token == input.CartToken, ct)
            ?? throw new NotFoundException($"Cart '{input.CartToken}' not found.");
        var items = cart.ActiveItems.ToArray();
        if (items.Length == 0) throw new DomainException("Cannot check out an empty cart.");
        foreach (var item in items)
        {
            if (item.ProductVariant is null) throw new DomainException("Cart references a variant that no longer exists.");
            if (item.ProductVariant.Product is { IsActive: false })
                throw new DomainException($"'{item.ProductVariant.Product.Name}' is no longer available for sale.");
        }
        var linePrices = await variantLinePricing.CalculateAsync(items, ct);
        var currency = linePrices[items[0].Id].AppliedPrice.Currency;
        var subtotal = items.Aggregate(Money.Zero(currency), (sum, item) => sum.Add(linePrices[item.Id].AppliedPrice.Multiply(item.Quantity)));
        var resolvedAddress = await ResolveShippingAddressAsync(input, ct);
        // Normalize a checkout copy. Never rewrite the customer's saved address record.
        var shippingAddress = new Address { FullName = resolvedAddress.FullName, Line1 = resolvedAddress.Line1,
            Line2 = resolvedAddress.Line2, City = resolvedAddress.City, Region = resolvedAddress.Region,
            PostalCode = resolvedAddress.PostalCode, Country = resolvedAddress.Country.Trim().ToUpperInvariant() };
        var shippingMethod = await ResolveShippingMethodAsync(input.ShippingMethodCode, ct);

        DiscountCode? discount = null;
        if (!string.IsNullOrWhiteSpace(input.DiscountCode))
        {
            var code = input.DiscountCode.Trim();
            var discounts = tracking ? db.DiscountCodes.AsTracking() : db.DiscountCodes.AsNoTracking();
            discount = await discounts.FirstOrDefaultAsync(d => d.Code == code, ct)
                ?? throw new InvalidDiscountException($"Discount code '{code}' does not exist.");
            if (!discount.IsRedeemable(now)) throw new InvalidDiscountException($"Discount code '{code}' is not redeemable.");
        }
        GiftCard? giftCard = null;
        if (!string.IsNullOrWhiteSpace(input.GiftCardCode))
        {
            var code = input.GiftCardCode.Trim().ToUpperInvariant();
            var cards = tracking ? db.GiftCards.AsTracking() : db.GiftCards.AsNoTracking();
            giftCard = await cards.FirstOrDefaultAsync(g => g.Code == code, ct)
                ?? throw new InvalidGiftCardException($"Gift card '{code}' does not exist.");
            if (!giftCard.IsRedeemable(now)) throw new InvalidGiftCardException($"Gift card '{code}' is not redeemable.");
            if (!string.Equals(giftCard.Currency, currency, StringComparison.Ordinal))
                throw new InvalidGiftCardException($"Gift card '{code}' is in {giftCard.Currency}, not {currency}.");
        }
        // Observe availability without changing any entity, even when tracking is requested by checkout.
        foreach (var item in items)
        {
            var inventory = item.ProductVariant!.Inventory
                ?? throw new InsufficientStockException($"No inventory record for '{item.ProductVariant.Sku}'.");
            if (inventory.QuantityAvailable < item.Quantity)
                throw new InsufficientStockException($"Insufficient stock for '{item.ProductVariant.Sku}'.");
        }
        var discountAmount = discount?.CalculateDiscount(subtotal) ?? Money.Zero(currency);
        var discountedSubtotal = subtotal.Subtract(discountAmount);
        var discountRate = subtotal.Amount > 0 ? discountAmount.Amount / subtotal.Amount : 0m;
        var taxLines = items.Select(i => new TaxLine(i.ProductVariant!.Product?.TaxCategoryId,
            linePrices[i.Id].AppliedPrice.Amount * i.Quantity * (1 - discountRate))).ToArray();
        var taxAmount = await taxService.CalculateTaxAsync(shippingAddress, taxLines, currency, ct);
        // Widen before multiplication: legal per-line weights can exceed an Int32 cart total.
        long weight = 0;
        try
        {
            foreach (var item in items)
            {
                if (item.ProductVariant!.WeightGrams < 0)
                    throw new InvalidShippingMethodException("A cart variant has a negative shipping weight.");
                weight = checked(weight + checked((long)item.ProductVariant.WeightGrams * item.Quantity));
            }
        }
        catch (OverflowException) { throw new InvalidShippingMethodException("The cart's total shipping weight is unsupported."); }
        await shippingRules.EnsureEligibleAsync(shippingMethod.Id, shippingAddress.Country, weight, ct);
        var deliveryDates = await shippingRules.DeliveryDatesAsync(now, shippingMethod, ct);
        var shippingAmount = shippingMethod.CalculateCharge(discountedSubtotal, weight);
        var total = discountedSubtotal.Add(taxAmount).Add(shippingAmount);
        var giftApplied = giftCard is null ? 0m : Math.Min(giftCard.Balance, total.Amount);
        return new(now, cart, items, shippingAddress, shippingMethod, discount, giftCard, subtotal, discountAmount,
            taxAmount, shippingAmount, total, giftApplied, new Money(total.Amount - giftApplied, currency), weight,
            deliveryDates.From, deliveryDates.To) { LinePrices = linePrices };
    }

    private async Task<Address> ResolveShippingAddressAsync(CheckoutPricingInput input, CancellationToken ct)
    {
        if (input.ShippingAddressId is not { } addressId)
            return input.ShippingAddress ?? throw new DomainException("A shipping address is required.");
        if (input.CustomerId is not { } customerId) throw new DomainException("Sign in to use a saved address.");
        var saved = await db.CustomerAddresses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId, ct)
            ?? throw new NotFoundException($"Saved address '{addressId}' not found.");
        return new Address { FullName = saved.Address.FullName, Line1 = saved.Address.Line1, Line2 = saved.Address.Line2,
            City = saved.Address.City, Region = saved.Address.Region, PostalCode = saved.Address.PostalCode, Country = saved.Address.Country };
    }

    private async Task<ShippingMethod> ResolveShippingMethodAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return await db.ShippingMethods.AsNoTracking().FirstOrDefaultAsync(m => m.IsDefault && m.IsActive, ct)
                ?? throw new InvalidShippingMethodException("No default shipping method is configured.");
        var normalized = code.Trim().ToLowerInvariant();
        var method = await db.ShippingMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Code == normalized, ct)
            ?? throw new InvalidShippingMethodException($"Shipping method '{normalized}' does not exist.");
        if (!method.IsActive) throw new InvalidShippingMethodException($"Shipping method '{normalized}' is not available.");
        return method;
    }
}
