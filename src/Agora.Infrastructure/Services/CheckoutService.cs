using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record CheckoutInput(
    string CartToken,
    string Email,
    Address? ShippingAddress,
    string? DiscountCode,
    string PaymentToken,
    Guid? CustomerId = null,
    string? ShippingMethodCode = null,
    Guid? ShippingAddressId = null,
    string? GiftCardCode = null,
    bool UseSavedPreferences = false);

public sealed record CheckoutResult(Order Order, string? GuestOrderAccessToken,
    DateTimeOffset? GuestOrderAccessExpiresAt);

/// <summary>
/// Turns a cart into a paid order:
/// validate -> reserve stock -> persist pending order -> charge gateway ->
/// commit reservations + mark paid (or release reservations on decline).
/// Totals follow discounts -> tax -> gift card tender: zone-based tax is
/// computed on the discounted lines, and any gift card is applied to the
/// final total with only the remainder charged to the gateway.
/// </summary>
public class CheckoutService(
    AgoraDbContext db,
    CheckoutPricingService pricingService,
    IPaymentGateway paymentGateway,
    WebhookService webhookService,
    GuestOrderAccessService guestOrderAccess)
{
    public async Task<CheckoutResult> CheckoutAsync(CheckoutInput input, CancellationToken ct = default)
    {
        var pricing = await pricingService.CalculateAsync(new CheckoutPricingInput(input.CartToken,
            input.ShippingAddress, input.DiscountCode, input.CustomerId, input.ShippingMethodCode,
            input.ShippingAddressId, input.GiftCardCode, input.UseSavedPreferences), tracking: true, ct);
        var now = pricing.CalculatedAt;
        var cart = pricing.Cart;
        var items = pricing.Items;
        var shippingAddress = pricing.ShippingAddress;
        var shippingMethod = pricing.ShippingMethod;
        var discount = pricing.Discount;
        var giftCard = pricing.GiftCard;
        var subtotal = pricing.Subtotal;
        var discountAmount = pricing.DiscountAmount;
        var taxAmount = pricing.TaxAmount;
        var shippingAmount = pricing.ShippingAmount;
        var total = pricing.Total;
        var currency = total.Currency;
        var giftCardApplied = pricing.GiftCardApplied;
        var chargeAmount = pricing.ChargeAmount;

        // Quote stops at calculation. Checkout alone starts these mutations.
        foreach (var item in items) item.ProductVariant!.Inventory!.Reserve(item.Quantity);

        var order = new Order
        {
            Number = GenerateOrderNumber(now),
            Email = input.Email.Trim(),
            CustomerId = input.CustomerId ?? cart.CustomerId,
            ShippingAddress = shippingAddress,
            ShippingMethodCode = shippingMethod.Code,
            ShippingMethodName = shippingMethod.Name,
            EstimatedDeliveryFrom = pricing.EstimatedDeliveryFrom,
            EstimatedDeliveryTo = pricing.EstimatedDeliveryTo,
            Currency = currency,
            Subtotal = subtotal.Amount,
            DiscountAmount = discountAmount.Amount,
            TaxAmount = taxAmount.Amount,
            ShippingAmount = shippingAmount.Amount,
            Total = total.Amount,
            DiscountCode = discount?.Code,
            GiftCardCode = giftCard?.Code,
            GiftCardAmount = giftCardApplied,
            CreatedAt = now,
        };

        foreach (var item in items)
        {
            var variant = item.ProductVariant!;
            var appliedPrice = pricing.LinePrices[item.Id].AppliedPrice;
            order.Items.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductVariantId = variant.Id,
                Sku = variant.Sku,
                ProductName = variant.Product?.Name ?? string.Empty,
                VariantName = variant.Name,
                UnitPrice = appliedPrice.Amount,
                Quantity = item.Quantity,
                LineTotal = appliedPrice.Multiply(item.Quantity).Amount,
            });
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct); // reservations + pending order persisted together

        // A fully gift-card-covered order never touches the gateway.
        string transactionId;
        if (chargeAmount.Amount > 0)
        {
            var payment = await paymentGateway.ChargeAsync(
                order.Number, chargeAmount, input.PaymentToken, ct);
            if (!payment.Success)
            {
                foreach (var item in items)
                {
                    item.ProductVariant!.Inventory!.ReleaseReservation(item.Quantity);
                }

                db.Orders.Remove(order);
                await db.SaveChangesAsync(ct); // gift card untouched: nothing redeemed yet
                throw new PaymentFailedException($"Payment declined ({payment.FailureReason}).");
            }

            transactionId = payment.TransactionId!;
        }
        else
        {
            // Nothing to charge: either a gift card covers the whole total, or
            // discounts (with free/zero-rate shipping and tax) reduced it to zero.
            transactionId = giftCard is not null
                ? $"gift_{giftCard.Code}"
                : $"free_{order.Number}";
        }

        // The provider call is already complete. Keep only local writes and subscriber
        // selection inside this transaction; a worker transports committed events later.
        await using var completion = await db.Database.BeginTransactionAsync(ct);
        if (giftCardApplied > 0)
        {
            GiftCardAccounting.Redeem(db, giftCard!, giftCardApplied, order.Id, now);
        }

        // Plaintext stays only in this in-memory issuance result. Its digest is
        // committed in the same save as paid state and inventory consumption.
        var guestIssue = order.CustomerId is null ? guestOrderAccess.Issue(order) : null;
        order.MarkPaid(transactionId, now);
        foreach (var item in items)
        {
            item.ProductVariant!.Inventory!.CommitReservation(item.Quantity);
        }

        discount?.RegisterUse(now);
        cart.RemoveActiveItems();
        await webhookService.StageAsync(WebhookEvents.OrderCreated, WebhookService.OrderPayload(order), now, ct);
        await webhookService.StageAsync(WebhookEvents.OrderPaid, WebhookService.OrderPayload(order), now, ct);
        await db.SaveChangesAsync(ct);
        await completion.CommitAsync(ct);

        return new CheckoutResult(order, guestIssue?.Token, guestIssue?.Credential.ExpiresAt);
    }

    private static string GenerateOrderNumber(DateTimeOffset now) =>
        $"ORD-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}
