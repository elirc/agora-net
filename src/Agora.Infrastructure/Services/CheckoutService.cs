using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record CheckoutInput(
    string CartToken,
    string Email,
    Address ShippingAddress,
    string? DiscountCode,
    string PaymentToken,
    Guid? CustomerId = null);

/// <summary>
/// Turns a cart into a paid order:
/// validate -> reserve stock -> persist pending order -> charge gateway ->
/// commit reservations + mark paid (or release reservations on decline).
/// </summary>
public class CheckoutService(
    AgoraDbContext db,
    ITaxCalculator taxCalculator,
    IShippingCalculator shippingCalculator,
    IPaymentGateway paymentGateway)
{
    public async Task<Order> CheckoutAsync(CheckoutInput input, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var cart = await db.Carts
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v!.Product)
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v!.Inventory)
            .FirstOrDefaultAsync(c => c.Token == input.CartToken, ct)
            ?? throw new NotFoundException($"Cart '{input.CartToken}' not found.");

        if (cart.Items.Count == 0)
        {
            throw new DomainException("Cannot check out an empty cart.");
        }

        foreach (var item in cart.Items)
        {
            if (item.ProductVariant is null)
            {
                throw new DomainException("Cart references a variant that no longer exists.");
            }

            if (item.ProductVariant.Product is { IsActive: false })
            {
                throw new DomainException(
                    $"'{item.ProductVariant.Product.Name}' is no longer available for sale.");
            }
        }

        var currency = cart.Items[0].ProductVariant!.Price.Currency;
        var subtotal = cart.Items.Aggregate(
            Money.Zero(currency),
            (acc, item) => acc.Add(item.ProductVariant!.Price.Multiply(item.Quantity)));

        // Validate the discount before touching stock so failures are side-effect free.
        DiscountCode? discount = null;
        if (!string.IsNullOrWhiteSpace(input.DiscountCode))
        {
            var code = input.DiscountCode.Trim();
            discount = await db.DiscountCodes.FirstOrDefaultAsync(d => d.Code == code, ct)
                ?? throw new InvalidDiscountException($"Discount code '{code}' does not exist.");
            if (!discount.IsRedeemable(now))
            {
                throw new InvalidDiscountException($"Discount code '{code}' is not redeemable.");
            }
        }

        // Reserve stock for every line (throws InsufficientStockException).
        foreach (var item in cart.Items)
        {
            var inventory = item.ProductVariant!.Inventory
                ?? throw new InsufficientStockException(
                    $"No inventory record for '{item.ProductVariant.Sku}'.");
            inventory.Reserve(item.Quantity);
        }

        var discountAmount = discount?.CalculateDiscount(subtotal) ?? Money.Zero(currency);
        var discountedSubtotal = subtotal.Subtract(discountAmount);
        var taxAmount = taxCalculator.CalculateTax(discountedSubtotal);
        var shippingAmount = shippingCalculator.CalculateShipping(
            discountedSubtotal, cart.Items.Sum(i => i.Quantity));
        var total = discountedSubtotal.Add(taxAmount).Add(shippingAmount);

        var order = new Order
        {
            Number = GenerateOrderNumber(now),
            Email = input.Email.Trim(),
            CustomerId = input.CustomerId ?? cart.CustomerId,
            ShippingAddress = input.ShippingAddress,
            Currency = currency,
            Subtotal = subtotal.Amount,
            DiscountAmount = discountAmount.Amount,
            TaxAmount = taxAmount.Amount,
            ShippingAmount = shippingAmount.Amount,
            Total = total.Amount,
            DiscountCode = discount?.Code,
            CreatedAt = now,
        };

        foreach (var item in cart.Items)
        {
            var variant = item.ProductVariant!;
            order.Items.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductVariantId = variant.Id,
                Sku = variant.Sku,
                ProductName = variant.Product?.Name ?? string.Empty,
                VariantName = variant.Name,
                UnitPrice = variant.Price.Amount,
                Quantity = item.Quantity,
                LineTotal = variant.Price.Multiply(item.Quantity).Amount,
            });
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct); // reservations + pending order persisted together

        var payment = await paymentGateway.ChargeAsync(order.Number, total, input.PaymentToken, ct);
        if (!payment.Success)
        {
            foreach (var item in cart.Items)
            {
                item.ProductVariant!.Inventory!.ReleaseReservation(item.Quantity);
            }

            db.Orders.Remove(order);
            await db.SaveChangesAsync(ct);
            throw new PaymentFailedException($"Payment declined ({payment.FailureReason}).");
        }

        order.MarkPaid(payment.TransactionId!, now);
        foreach (var item in cart.Items)
        {
            item.ProductVariant!.Inventory!.CommitReservation(item.Quantity);
        }

        discount?.RegisterUse(now);
        cart.Clear();
        await db.SaveChangesAsync(ct);

        return order;
    }

    private static string GenerateOrderNumber(DateTimeOffset now) =>
        $"ORD-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}
