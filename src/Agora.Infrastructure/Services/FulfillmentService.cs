using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record FulfillmentLineInput(Guid OrderItemId, int Quantity);

/// <summary>
/// Creates shipments for paid orders and derives the order status from
/// cumulative shipment coverage (PartiallyFulfilled until every line is
/// covered, then Fulfilled).
/// </summary>
public class FulfillmentService(AgoraDbContext db, WebhookService webhookService, TimeProvider clock)
{
    public const string DefaultCarrier = "Manual";

    /// <summary>
    /// Ships the given line quantities; a null/empty line list ships
    /// everything still outstanding.
    /// </summary>
    public async Task<Fulfillment> CreateAsync(
        string orderNumber,
        string? carrier,
        string? trackingNumber,
        IReadOnlyList<FulfillmentLineInput>? lines,
        CancellationToken ct = default,
        Guid? assignmentId = null,
        Guid? actorId = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Number == orderNumber, ct)
            ?? throw new NotFoundException($"Order '{orderNumber}' not found.");

        if (order.Status is not (OrderStatus.Paid or OrderStatus.PartiallyFulfilled))
        {
            throw new InvalidOrderStateException(
                $"Order {order.Number} cannot be fulfilled from status {order.Status}.");
        }

        if (await db.Set<OrderHold>().AnyAsync(hold => hold.OrderId == order.Id && hold.IsActive, ct))
            throw new WarehouseCoordinationConflictException("This order has an active operational hold.");

        var assignment = await db.Set<WarehouseAssignment>()
            .SingleOrDefaultAsync(slot => slot.OrderId == order.Id, ct);
        if (assignment is not null && assignment.IsLive(now))
        {
            if (assignmentId is null || actorId is null)
                throw new WarehouseCoordinationConflictException("This order has a live warehouse assignment.");
            assignment.Authorize(actorId.Value, assignmentId.Value, assignment.Revision, now);
        }
        else if (assignmentId is not null)
        {
            throw new WarehouseCoordinationConflictException("The supplied warehouse assignment is expired or stale.");
        }

        var alreadyFulfilled = await FulfilledQuantitiesAsync(order.Id, ct);

        var effectiveLines = lines is { Count: > 0 }
            ? lines
            : order.Items
                .Select(i => new FulfillmentLineInput(
                    i.Id, i.Quantity - alreadyFulfilled.GetValueOrDefault(i.Id)))
                .Where(l => l.Quantity > 0)
                .ToList();

        if (effectiveLines.Count == 0)
        {
            throw new InvalidFulfillmentException("Nothing left to fulfill on this order.");
        }

        if (effectiveLines.Select(l => l.OrderItemId).Distinct().Count() != effectiveLines.Count)
        {
            throw new InvalidFulfillmentException("Duplicate order lines in fulfillment.");
        }

        var fulfillment = new Fulfillment
        {
            Number = $"FUL-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            OrderId = order.Id,
            Carrier = string.IsNullOrWhiteSpace(carrier) ? DefaultCarrier : carrier.Trim(),
            TrackingNumber = string.IsNullOrWhiteSpace(trackingNumber)
                ? null
                : trackingNumber.Trim(),
            CreatedAt = now,
        };

        foreach (var line in effectiveLines)
        {
            var orderItem = order.Items.FirstOrDefault(i => i.Id == line.OrderItemId)
                ?? throw new InvalidFulfillmentException(
                    $"Order line '{line.OrderItemId}' does not belong to order {order.Number}.");

            var remaining = orderItem.Quantity - alreadyFulfilled.GetValueOrDefault(orderItem.Id);
            if (line.Quantity < 1 || line.Quantity > remaining)
            {
                throw new InvalidFulfillmentException(
                    $"Cannot ship {line.Quantity} of '{orderItem.Sku}': {remaining} remaining.");
            }

            fulfillment.Items.Add(new FulfillmentItem
            {
                FulfillmentId = fulfillment.Id,
                OrderItemId = orderItem.Id,
                ProductVariantId = orderItem.ProductVariantId,
                Sku = orderItem.Sku,
                Quantity = line.Quantity,
            });
        }

        db.Fulfillments.Add(fulfillment);

        // Derive the order status from total coverage including this shipment.
        var covered = new Dictionary<Guid, int>(alreadyFulfilled);
        foreach (var item in fulfillment.Items)
        {
            covered[item.OrderItemId] = covered.GetValueOrDefault(item.OrderItemId) + item.Quantity;
        }

        var fullyCovered = order.Items.All(i => covered.GetValueOrDefault(i.Id) >= i.Quantity);
        if (fullyCovered)
        {
            order.MarkFulfilled(now);
            if (assignment is not null) db.Remove(assignment);
        }
        else
        {
            order.MarkPartiallyFulfilled();
        }

        if (fullyCovered)
        {
            await webhookService.StageAsync(
                WebhookEvents.OrderFulfilled, WebhookService.OrderPayload(order), now, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return fulfillment;
    }

    private async Task<Dictionary<Guid, int>> FulfilledQuantitiesAsync(
        Guid orderId, CancellationToken ct) =>
        await db.FulfillmentItems
            .Where(i => i.Fulfillment!.OrderId == orderId)
            .GroupBy(i => i.OrderItemId)
            .Select(g => new { g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, ct);
}
