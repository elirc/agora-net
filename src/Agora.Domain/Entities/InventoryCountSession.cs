using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum InventoryCountStatus { Open, Applied, Cancelled }

public sealed class InventoryCountSession
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public InventoryCountStatus Status { get; private set; } = InventoryCountStatus.Open;
    public long Revision { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? AppliedBy { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public List<InventoryCountLine> Lines { get; private set; } = [];
    private InventoryCountSession() { }
    public InventoryCountSession(Guid actor, DateTimeOffset now, IEnumerable<InventoryCountLine> lines)
    { var copy=lines.ToList(); if(actor==Guid.Empty || copy.Count is <1 or >100 || copy.Select(x=>x.ProductVariantId).Distinct().Count()!=copy.Count) throw new DomainException("A count session requires an actor and 1–100 distinct variants."); CreatedBy=actor; CreatedAt=now; Lines=copy; }
    public void Record(Guid lineId, int count, long expectedRevision)
    { Expected(expectedRevision); if(Status!=InventoryCountStatus.Open) throw new InventoryCountConflictException("Only an open session can be edited."); if(count is <0 or >1_000_000) throw new InvalidInventoryCountException("A count must be from 0 to 1,000,000."); (Lines.SingleOrDefault(x=>x.Id==lineId) ?? throw new NotFoundException("Count line was not found.")).Record(count); Advance(); }
    public void Apply(Guid actor, DateTimeOffset now, long expectedRevision)
    { Expected(expectedRevision); if(actor==Guid.Empty)throw new DomainException("An applying actor is required.");if(Status!=InventoryCountStatus.Open) throw new InventoryCountConflictException("Only an open session can be applied."); if(Lines.Any(x=>x.CountedQuantity is null)) throw new InvalidInventoryCountException("Every line must be counted before applying the session."); AppliedBy=actor; AppliedAt=now; Status=InventoryCountStatus.Applied; Advance(); }
    public void Cancel(Guid actor, DateTimeOffset now, long expectedRevision)
    { Expected(expectedRevision); if(actor==Guid.Empty)throw new DomainException("A cancelling actor is required.");if(Status!=InventoryCountStatus.Open) throw new InventoryCountConflictException("Only an open session can be cancelled."); CancelledBy=actor; CancelledAt=now; Status=InventoryCountStatus.Cancelled; Advance(); }
    public void Expected(long expected) { if(expected!=Revision) throw new InventoryCountConflictException("The count session changed. Reload its revision."); }
    private void Advance()=>Revision=checked(Revision+1);
}

public sealed class InventoryCountLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid SessionId { get; private set; }
    public Guid? ProductVariantId { get; private set; }
    public string Sku { get; private set; } = "";
    public int BaselineOnHand { get; private set; }
    public int BaselineReserved { get; private set; }
    public int BaselineVersion { get; private set; }
    public int? CountedQuantity { get; private set; }
    public int? AppliedOnHand { get; private set; }
    public int? Difference { get; private set; }
    private InventoryCountLine() { }
    public InventoryCountLine(Guid variantId,string sku,int onHand,int reserved,int version)
    { var clean=sku.Trim();if(variantId==Guid.Empty||clean.Length is<1 or>64||onHand<0||reserved<0||reserved>onHand||version<0)throw new DomainException("A count line requires a valid inventory baseline.");ProductVariantId=variantId;Sku=clean;BaselineOnHand=onHand;BaselineReserved=reserved;BaselineVersion=version; }
    public void Record(int count)=>CountedQuantity=count;
    public void RecordApplication(int after) { AppliedOnHand=after; Difference=checked(after-BaselineOnHand); }
}

public sealed class InventoryCountConflictException(string message) : DomainException(message);
public sealed class InvalidInventoryCountException(string message) : DomainException(message);
