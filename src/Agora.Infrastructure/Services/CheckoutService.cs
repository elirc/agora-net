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
    Guid? ShippingAddressId = null);

/// <summary>
/// Turns a cart into a paid order:
/// validate -> reserve stock -> persist pending order -> charge gateway ->
/// commit reservations + mark paid (or release reservations on decline).
/// Shipping is priced by the selected <see cref="ShippingMethod"/> (or the
/// configured default) and the address may come from the customer's address book.
/// </summary>
public class CheckoutService(
    AgoraDbContext db,
    ITaxCalculator taxCalculator,
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

        // Saved-for-later lines stay behind; checkout covers active lines only.
        var items = cart.ActiveItems.ToList();
        if (items.Count == 0)
        {
            throw new DomainException("Cannot check out an empty cart.");
        }

        foreach (var item in items)
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

        var currency = items[0].ProductVariant!.Price.Currency;
        var subtotal = items.Aggregate(
            Money.Zero(currency),
            (acc, item) => acc.Add(item.ProductVariant!.Price.Multiply(item.Quantity)));

        // Resolve address and shipping method before touching stock so failures
        // are side-effect free.
        var shippingAddress = await ResolveShippingAddressAsync(input, ct);
        var shippingMethod = await ResolveShippingMethodAsync(input.ShippingMethodCode, ct);

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
        foreach (var item in items)
        {
            var inventory = item.ProductVariant!.Inventory
                ?? throw new InsufficientStockException(
                    $"No inventory record for '{item.ProductVariant.Sku}'.");
            inventory.Reserve(item.Quantity);
        }

        var discountAmount = discount?.CalculateDiscount(subtotal) ?? Money.Zero(currency);
        var discountedSubtotal = subtotal.Subtract(discountAmount);
        var taxAmount = taxCalculator.CalculateTax(discountedSubtotal);
        var totalWeightGrams = items.Sum(i => i.ProductVariant!.WeightGrams * i.Quantity);
        var shippingAmount = shippingMethod.CalculateCharge(discountedSubtotal, totalWeightGrams);
        var total = discountedSubtotal.Add(taxAmount).Add(shippingAmount);

        var order = new Order
        {
            Number = GenerateOrderNumber(now),
            Email = input.Email.Trim(),
            CustomerId = input.CustomerId ?? cart.CustomerId,
            ShippingAddress = shippingAddress,
            ShippingMethodCode = shippingMethod.Code,
            ShippingMethodName = shippingMethod.Name,
            EstimatedDeliveryFrom = now.AddDays(shippingMethod.MinDays),
            EstimatedDeliveryTo = now.AddDays(shippingMethod.MaxDays),
            Currency = currency,
            Subtotal = subtotal.Amount,
            DiscountAmount = discountAmount.Amount,
            TaxAmount = taxAmount.Amount,
            ShippingAmount = shippingAmount.Amount,
            Total = total.Amount,
            DiscountCode = discount?.Code,
            CreatedAt = now,
        };

        foreach (var item in items)
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
            foreach (var item in items)
            {
                item.ProductVariant!.Inventory!.ReleaseReservation(item.Quantity);
            }

            db.Orders.Remove(order);
            await db.SaveChangesAsync(ct);
            throw new PaymentFailedException($"Payment declined ({payment.FailureReason}).");
        }

        order.MarkPaid(payment.TransactionId!, now);
        foreach (var item in items)
        {
            item.ProductVariant!.Inventory!.CommitReservation(item.Quantity);
        }

        discount?.RegisterUse(now);
        cart.RemoveActiveItems();
        await db.SaveChangesAsync(ct);

        return order;
    }

    /// <summary>
    /// Uses the inline address, or copies one from the signed-in customer's
    /// address book when <see cref="CheckoutInput.ShippingAddressId"/> is given.
    /// </summary>
    private async Task<Address> ResolveShippingAddressAsync(CheckoutInput input, CancellationToken ct)
    {
        if (input.ShippingAddressId is not { } addressId)
        {
            return input.ShippingAddress
                ?? throw new DomainException("A shipping address is required.");
        }

        if (input.CustomerId is not { } customerId)
        {
            throw new DomainException("Sign in to use a saved address.");
        }

        var saved = await db.CustomerAddresses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId, ct)
            ?? throw new NotFoundException($"Saved address '{addressId}' not found.");

        // Copy: the order owns its own address snapshot.
        return new Address
        {
            FullName = saved.Address.FullName,
            Line1 = saved.Address.Line1,
            Line2 = saved.Address.Line2,
            City = saved.Address.City,
            Region = saved.Address.Region,
            PostalCode = saved.Address.PostalCode,
            Country = saved.Address.Country,
        };
    }

    /// <summary>Resolves the requested shipping method, or the active default when omitted.</summary>
    private async Task<ShippingMethod> ResolveShippingMethodAsync(string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return await db.ShippingMethods
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.IsDefault && m.IsActive, ct)
                ?? throw new InvalidShippingMethodException("No default shipping method is configured.");
        }

        var normalized = code.Trim().ToLowerInvariant();
        var method = await db.ShippingMethods
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Code == normalized, ct)
            ?? throw new InvalidShippingMethodException($"Shipping method '{normalized}' does not exist.");

        if (!method.IsActive)
        {
            throw new InvalidShippingMethodException($"Shipping method '{normalized}' is not available.");
        }

        return method;
    }

    private static string GenerateOrderNumber(DateTimeOffset now) =>
        $"ORD-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}
