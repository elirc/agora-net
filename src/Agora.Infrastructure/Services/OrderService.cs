using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

/// <summary>Order lifecycle operations beyond checkout.</summary>
public class OrderService(AgoraDbContext db, IPaymentGateway paymentGateway)
{
    /// <summary>Cancels a pending or paid order; paid orders are refunded and restocked.</summary>
    public async Task<Order> CancelAsync(string number, CancellationToken ct = default)
    {
        var order = await LoadAsync(number, ct);
        var wasPaid = order.Status == OrderStatus.Paid;

        order.Cancel(DateTimeOffset.UtcNow);

        if (wasPaid)
        {
            await RefundTenderAsync(order, ct);
            await RestockAsync(order, ct);
        }

        await db.SaveChangesAsync(ct);
        return order;
    }

    /// <summary>Refunds a paid or fulfilled order via the gateway and restocks its items.</summary>
    public async Task<Order> RefundAsync(string number, CancellationToken ct = default)
    {
        var order = await LoadAsync(number, ct);

        // A full refund on top of an accepted partial return would over-refund.
        var hasAcceptedReturns = await db.ReturnRequests.AnyAsync(
            r => r.OrderId == order.Id && r.Status == ReturnStatus.Approved, ct);
        if (hasAcceptedReturns)
        {
            throw new InvalidOrderStateException(
                $"Order {order.Number} has approved returns; refund the remaining lines via RMAs instead.");
        }

        order.Refund(DateTimeOffset.UtcNow);

        await RefundTenderAsync(order, ct);
        await RestockAsync(order, ct);

        await db.SaveChangesAsync(ct);
        return order;
    }

    public async Task<Order?> FindAsync(string number, CancellationToken ct = default) =>
        await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Number == number, ct);

    private async Task<Order> LoadAsync(string number, CancellationToken ct) =>
        await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Number == number, ct)
        ?? throw new NotFoundException($"Order '{number}' not found.");

    /// <summary>
    /// Returns each tender to its source: the gateway charge is refunded and
    /// any gift card portion is credited back to the card.
    /// </summary>
    private async Task RefundTenderAsync(Order order, CancellationToken ct)
    {
        var chargedAmount = order.Total - order.GiftCardAmount;
        if (chargedAmount > 0)
        {
            await paymentGateway.RefundAsync(
                order.PaymentTransactionId!, new Money(chargedAmount, order.Currency), ct);
        }

        if (order.GiftCardAmount > 0 && order.GiftCardCode is { } cardCode)
        {
            var giftCard = await db.GiftCards.FirstOrDefaultAsync(g => g.Code == cardCode, ct);
            giftCard?.Credit(order.GiftCardAmount);
        }
    }

    private async Task RestockAsync(Order order, CancellationToken ct)
    {
        var variantIds = order.Items.Select(i => i.ProductVariantId).ToList();
        var inventories = await db.InventoryItems
            .Where(i => variantIds.Contains(i.ProductVariantId))
            .ToDictionaryAsync(i => i.ProductVariantId, ct);

        foreach (var item in order.Items)
        {
            // The variant may have been deleted since purchase; skip silently.
            if (inventories.TryGetValue(item.ProductVariantId, out var inventory))
            {
                inventory.Restock(item.Quantity);
            }
        }
    }
}
