using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record ReturnLineInput(Guid OrderItemId, int Quantity);

public sealed record CreateReturnInput(
    string OrderNumber,
    ReturnReason Reason,
    string? Comment,
    IReadOnlyList<ReturnLineInput> Lines,
    Guid? CustomerId,
    string? Email);

/// <summary>
/// RMA lifecycle: create (fulfilled orders only, quantities capped by what is
/// still returnable), approve (partial refund via the payment gateway +
/// restock), reject, and requester cancellation.
/// </summary>
public class ReturnService(AgoraDbContext db, IPaymentGateway paymentGateway)
{
    public async Task<ReturnRequest> CreateAsync(CreateReturnInput input, CancellationToken ct = default)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Number == input.OrderNumber, ct)
            ?? throw new NotFoundException($"Order '{input.OrderNumber}' not found.");

        EnsureRequesterOwnsOrder(order, input.CustomerId, input.Email);

        if (order.Status != OrderStatus.Fulfilled)
        {
            throw new InvalidOrderStateException(
                $"Order {order.Number} cannot be returned from status {order.Status}; " +
                "only fulfilled orders are returnable.");
        }

        if (input.Lines.Count == 0)
        {
            throw new InvalidReturnRequestException("A return needs at least one line.");
        }

        if (input.Lines.Select(l => l.OrderItemId).Distinct().Count() != input.Lines.Count)
        {
            throw new InvalidReturnRequestException("Duplicate order lines in return request.");
        }

        // Quantities already tied up in open or accepted returns count against the cap.
        var previouslyReturned = await db.ReturnRequestItems
            .Where(i => i.ReturnRequest!.OrderId == order.Id
                        && (i.ReturnRequest.Status == ReturnStatus.Requested
                            || i.ReturnRequest.Status == ReturnStatus.Approved))
            .GroupBy(i => i.OrderItemId)
            .Select(g => new { g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, ct);

        // Refunds follow the order's effective discount and tax rates so
        // partial returns compose with order-level discounts.
        var discountRate = order.Subtotal > 0 ? order.DiscountAmount / order.Subtotal : 0m;
        var discountedSubtotal = order.Subtotal - order.DiscountAmount;
        var taxRate = discountedSubtotal > 0 ? order.TaxAmount / discountedSubtotal : 0m;

        var now = DateTimeOffset.UtcNow;
        var request = new ReturnRequest
        {
            Number = $"RMA-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            OrderId = order.Id,
            CustomerId = input.CustomerId ?? order.CustomerId,
            Reason = input.Reason,
            Comment = input.Comment?.Trim() ?? string.Empty,
            Currency = order.Currency,
            CreatedAt = now,
        };

        foreach (var line in input.Lines)
        {
            var orderItem = order.Items.FirstOrDefault(i => i.Id == line.OrderItemId)
                ?? throw new InvalidReturnRequestException(
                    $"Order line '{line.OrderItemId}' does not belong to order {order.Number}.");

            var alreadyReturned = previouslyReturned.GetValueOrDefault(orderItem.Id);
            var returnable = orderItem.Quantity - alreadyReturned;
            if (line.Quantity < 1 || line.Quantity > returnable)
            {
                throw new InvalidReturnRequestException(
                    $"Cannot return {line.Quantity} of '{orderItem.Sku}': {returnable} returnable.");
            }

            var lineBase = orderItem.UnitPrice * line.Quantity;
            var lineDiscounted = lineBase * (1 - discountRate);
            var lineRefund = decimal.Round(
                lineDiscounted * (1 + taxRate), 2, MidpointRounding.AwayFromZero);

            request.Items.Add(new ReturnRequestItem
            {
                ReturnRequestId = request.Id,
                OrderItemId = orderItem.Id,
                ProductVariantId = orderItem.ProductVariantId,
                Sku = orderItem.Sku,
                Quantity = line.Quantity,
                RefundAmount = lineRefund,
            });
        }

        request.RefundAmount = request.Items.Sum(i => i.RefundAmount);

        db.ReturnRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return request;
    }

    /// <summary>Approves the RMA: refunds through the gateway and restocks the units.</summary>
    public async Task<ReturnRequest> ApproveAsync(string number, CancellationToken ct = default)
    {
        var request = await LoadAsync(number, ct);
        var order = request.Order!;

        if (request.Status != ReturnStatus.Requested)
        {
            throw new InvalidReturnStateException(
                $"Return {request.Number} cannot be approved from status {request.Status}.");
        }

        var refund = await paymentGateway.RefundAsync(
            order.PaymentTransactionId!, new Money(request.RefundAmount, request.Currency), ct);
        if (!refund.Success)
        {
            throw new PaymentFailedException($"Refund failed ({refund.FailureReason}).");
        }

        request.Approve(refund.TransactionId!, DateTimeOffset.UtcNow);

        var variantIds = request.Items.Select(i => i.ProductVariantId).ToList();
        var inventories = await db.InventoryItems
            .Where(i => variantIds.Contains(i.ProductVariantId))
            .ToDictionaryAsync(i => i.ProductVariantId, ct);
        foreach (var item in request.Items)
        {
            // Variant may have been deleted since purchase; skip silently.
            if (inventories.TryGetValue(item.ProductVariantId, out var inventory))
            {
                inventory.Restock(item.Quantity);
            }
        }

        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<ReturnRequest> RejectAsync(string number, string? note, CancellationToken ct = default)
    {
        var request = await LoadAsync(number, ct);
        request.Reject(note, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return request;
    }

    /// <summary>Requester cancellation while the RMA is still open.</summary>
    public async Task<ReturnRequest> CancelAsync(
        string number, Guid? customerId, string? email, CancellationToken ct = default)
    {
        var request = await LoadAsync(number, ct);
        EnsureRequesterOwnsOrder(request.Order!, customerId, email);
        request.Cancel(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return request;
    }

    private async Task<ReturnRequest> LoadAsync(string number, CancellationToken ct) =>
        await db.ReturnRequests
            .Include(r => r.Items)
            .Include(r => r.Order)
            .FirstOrDefaultAsync(r => r.Number == number, ct)
        ?? throw new NotFoundException($"Return '{number}' not found.");

    /// <summary>
    /// A requester must be the order's account owner or present the order's
    /// email (guest flow). Failures read as 404 to avoid leaking orders.
    /// </summary>
    private static void EnsureRequesterOwnsOrder(Order order, Guid? customerId, string? email)
    {
        if (customerId is { } id && order.CustomerId == id)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(email)
            && string.Equals(order.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new NotFoundException($"Order '{order.Number}' not found.");
    }
}
