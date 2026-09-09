using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum OrderHoldReason { AddressQuestion, StockInvestigation, CustomerRequest }

public sealed class OrderHold
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public OrderHoldReason Reason { get; private set; }
    public string? Note { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? ReleasedBy { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Revision { get; private set; }
    private OrderHold() { }
    public OrderHold(Guid orderId, OrderHoldReason reason, string? note, Guid actor, DateTimeOffset now)
    {
        if (orderId == Guid.Empty || actor == Guid.Empty || !Enum.IsDefined(reason)) throw new DomainException("A hold requires a valid order, reason, and actor.");
        var clean = note?.Trim();
        if (clean?.Length > 500)
            throw new DomainException("Hold note may contain at most 500 characters.");
        OrderId = orderId;
        Reason = reason;
        Note = string.IsNullOrEmpty(clean) ? null : clean;
        CreatedBy = actor;
        CreatedAt = now;
    }
    public void Release(Guid actor,long expectedRevision,DateTimeOffset now)
    {
        if (!IsActive) throw new WarehouseCoordinationConflictException("The hold is already released.");
        if (expectedRevision != Revision) throw new WarehouseCoordinationConflictException("The hold changed. Reload its revision.");
        if (actor == Guid.Empty) throw new DomainException("A releasing actor is required.");
        var nextRevision = checked(Revision + 1);
        IsActive = false;
        ReleasedBy = actor;
        ReleasedAt = now;
        Revision = nextRevision;
    }
}

public sealed class WarehouseAssignment
{
    public Guid OrderId { get; private set; }
    public Guid AssignmentId { get; private set; }
    public Guid OwnerId { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public long Revision { get; private set; }
    private WarehouseAssignment() { }
    public WarehouseAssignment(Guid orderId, Guid owner, DateTimeOffset now)
    {
        OrderId = orderId;
        Replace(owner, now);
    }
    public bool IsLive(DateTimeOffset now) => now < ExpiresAt;
    public void Replace(Guid owner, DateTimeOffset now)
    {
        if (OrderId == Guid.Empty || owner == Guid.Empty)
            throw new DomainException("An assignment requires an order and owner.");
        var nextRevision = checked(Revision + 1);
        AssignmentId = Guid.NewGuid();
        OwnerId = owner;
        ClaimedAt = now;
        ExpiresAt = now.AddMinutes(15);
        Revision = nextRevision;
    }
    public void Renew(Guid owner, Guid assignmentId, long expectedRevision, DateTimeOffset now)
    {
        Authorize(owner, assignmentId, expectedRevision, now);
        var nextRevision = checked(Revision + 1);
        ExpiresAt = now.AddMinutes(15);
        Revision = nextRevision;
    }
    public void Authorize(Guid owner, Guid assignmentId, long expectedRevision, DateTimeOffset now)
    {
        if (!IsLive(now) || OwnerId != owner || AssignmentId != assignmentId || Revision != expectedRevision)
            throw new WarehouseCoordinationConflictException("The warehouse assignment is expired, stale, or belongs to another administrator.");
    }
}
public sealed class WarehouseCoordinationConflictException(string message) : DomainException(message);
