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
    OrderAccessActor Actor);

/// <summary>
/// RMA lifecycle: create (fulfilled orders only, quantities capped by what is
/// still returnable), approve (partial refund via the payment gateway +
/// restock), reject, and requester cancellation.
/// </summary>
public class ReturnService(AgoraDbContext db, IPaymentGateway paymentGateway, ReturnEligibilityService eligibility,
    TimeProvider clock, GuestOrderAccessService orderAccess)
{
    public async Task<ReturnRequest> CreateAsync(CreateReturnInput input, CancellationToken ct = default)
    {
        // Creation has no external call. Serialize quantity observation and insertion locally.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Number == input.OrderNumber, ct)
            ?? throw new NotFoundException($"Order '{input.OrderNumber}' not found.");

        await orderAccess.EnsureCanReadAsync(order, input.Actor, ct);

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

        var evaluation = await eligibility.EvaluateAsync(order, now, ct);
        if (!evaluation.Eligible) throw new InvalidReturnRequestException(string.Join(", ", evaluation.Reasons));
        var remaining = evaluation.Lines.ToDictionary(l => l.OrderItemId, l => l.RemainingQuantity);
        var request = new ReturnRequest
        {
            Number = $"RMA-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            OrderId = order.Id,
            CustomerId = order.CustomerId,
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

            var returnable = remaining.GetValueOrDefault(orderItem.Id);
            if (line.Quantity < 1 || line.Quantity > returnable)
            {
                throw new InvalidReturnRequestException(
                    $"Cannot return {line.Quantity} of '{orderItem.Sku}': {returnable} returnable.");
            }

            var lineRefund = ReturnEligibilityRules.EstimateRefund(order, orderItem, line.Quantity);

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
        await transaction.CommitAsync(ct);
        return request;
    }

    /// <summary>
    /// Approves the RMA: refunds each tender to its source (gateway charge
    /// first, then gift card credit) and restocks the units.
    /// </summary>
    public async Task<ReturnRequest> ApproveAsync(string number, CancellationToken ct = default)
    {
        var request = await LoadAsync(number, ct);
        var order = request.Order!;

        if (request.Status != ReturnStatus.Requested)
        {
            throw new InvalidReturnStateException(
                $"Return {request.Number} cannot be approved from status {request.Status}.");
        }

        // Tender-aware split: the gateway was only charged Total - GiftCardAmount,
        // so refunds drain that gateway charge first (counting refunds already
        // issued for earlier approved RMAs) and credit the rest to the gift card.
        var gatewayCharged = order.Total - order.GiftCardAmount;
        var previouslyRefunded = await db.ReturnRequests
            .Where(r => r.OrderId == order.Id
                        && r.Status == ReturnStatus.Approved
                        && r.Id != request.Id)
            .SumAsync(r => (decimal?)r.RefundAmount, ct) ?? 0m;
        var gatewayRemaining = Math.Max(0m, gatewayCharged - previouslyRefunded);
        var gatewayPortion = Math.Min(request.RefundAmount, gatewayRemaining);
        var giftCardPortion = request.RefundAmount - gatewayPortion;

        string refundTransactionId;
        if (gatewayPortion > 0)
        {
            var refund = await paymentGateway.RefundAsync(
                order.PaymentTransactionId!, new Money(gatewayPortion, request.Currency), ct);
            if (!refund.Success)
            {
                throw new PaymentFailedException($"Refund failed ({refund.FailureReason}).");
            }

            refundTransactionId = refund.TransactionId!;
        }
        else
        {
            refundTransactionId = $"gcref_{Guid.NewGuid():N}";
        }

        if (giftCardPortion > 0 && order.GiftCardCode is { } cardCode)
        {
            var giftCard = await db.GiftCards.FirstOrDefaultAsync(g => g.Code == cardCode, ct);
            if (giftCard is not null)
                GiftCardAccounting.Credit(db, giftCard, giftCardPortion, order.Id, request.Id, clock.GetUtcNow());
        }

        request.Approve(refundTransactionId, clock.GetUtcNow());

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
        request.Reject(note, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return request;
    }

    /// <summary>Requester cancellation while the RMA is still open.</summary>
    public async Task<ReturnRequest> CancelAsync(
        string number, OrderAccessActor actor, CancellationToken ct = default)
    {
        var request = await LoadAsync(number, ct);
        await orderAccess.EnsureCanReadAsync(request.Order!, actor, ct);
        request.Cancel(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return request;
    }

    private async Task<ReturnRequest> LoadAsync(string number, CancellationToken ct) =>
        await db.ReturnRequests
            .Include(r => r.Items)
            .Include(r => r.Order)
            .FirstOrDefaultAsync(r => r.Number == number, ct)
        ?? throw new NotFoundException($"Return '{number}' not found.");

}
