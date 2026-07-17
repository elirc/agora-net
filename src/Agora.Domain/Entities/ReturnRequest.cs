using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum ReturnStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
}

public enum ReturnReason
{
    Damaged = 0,
    WrongItem = 1,
    NotAsDescribed = 2,
    NoLongerNeeded = 3,
    Other = 4,
}

/// <summary>
/// Return merchandise authorization (RMA) for one or more order lines.
/// Lifecycle: Requested -> Approved (refund issued + restock) or Rejected;
/// the requester may cancel while still Requested.
/// </summary>
public class ReturnRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid? CustomerId { get; set; }

    public ReturnStatus Status { get; private set; } = ReturnStatus.Requested;
    public ReturnReason Reason { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string? RejectionNote { get; private set; }

    /// <summary>Total refund issued on approval (discount- and tax-adjusted).</summary>
    public decimal RefundAmount { get; set; }
    public string Currency { get; set; } = Money.DefaultCurrency;
    public string? RefundTransactionId { get; private set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; private set; }

    public List<ReturnRequestItem> Items { get; set; } = [];

    public void Approve(string refundTransactionId, DateTimeOffset now)
    {
        EnsureRequested("approved");
        Status = ReturnStatus.Approved;
        RefundTransactionId = refundTransactionId;
        ProcessedAt = now;
    }

    public void Reject(string? note, DateTimeOffset now)
    {
        EnsureRequested("rejected");
        Status = ReturnStatus.Rejected;
        RejectionNote = note;
        ProcessedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureRequested("cancelled");
        Status = ReturnStatus.Cancelled;
        ProcessedAt = now;
    }

    private void EnsureRequested(string action)
    {
        if (Status != ReturnStatus.Requested)
        {
            throw new InvalidReturnStateException(
                $"Return {Number} cannot be {action} from status {Status}.");
        }
    }
}

/// <summary>A returned order line with the quantity being sent back.</summary>
public class ReturnRequestItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReturnRequestId { get; set; }
    public ReturnRequest? ReturnRequest { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid ProductVariantId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>This line's share of the refund.</summary>
    public decimal RefundAmount { get; set; }
}
