using System.Security.Cryptography;
using System.Text.Json;
using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum PurchaseOrderStatus { Draft, Ordered, PartiallyReceived, Received, Cancelled }

public sealed class Supplier
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = "";
    public string? Reference { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    private Supplier() { }
    public Supplier(string name, string? reference, DateTimeOffset now)
    {
        Name = Bounded(name, 120, "Supplier name");
        Reference = Optional(reference, 120, "Supplier reference");
        CreatedAt = now;
    }
    public void Deactivate() { if (!IsActive) return; IsActive = false; Version = checked(Version + 1); }
    public void AcceptNewPurchaseOrder()
    { if (!IsActive) throw new ProcurementConflictException("A deactivated supplier cannot accept a new purchase order."); Version = checked(Version + 1); }
    private static string Bounded(string value, int max, string label)
    { var v = value.Trim(); if (v.Length is < 1 || v.Length > max) throw new DomainException($"{label} must contain 1–{max} characters."); return v; }
    private static string? Optional(string? value, int max, string label)
    { if (value is null) return null; var v = value.Trim(); if (v.Length > max) throw new DomainException($"{label} may contain at most {max} characters."); return v.Length == 0 ? null : v; }
}

public sealed class PurchaseOrder
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid SupplierId { get; private set; }
    public Supplier? Supplier { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public long Revision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public List<PurchaseOrderLine> Lines { get; private set; } = [];
    public List<PurchaseOrderReceipt> Receipts { get; private set; } = [];
    private PurchaseOrder() { }
    public PurchaseOrder(Guid supplierId, IEnumerable<PurchaseOrderLine> lines, DateTimeOffset now)
    {
        if (supplierId == Guid.Empty) throw new DomainException("A supplier is required.");
        var copy = lines.ToList();
        if (copy.Count is < 1 or > 100 || copy.Select(x => x.ProductVariantId).Distinct().Count() != copy.Count)
            throw new DomainException("A purchase order needs 1–100 distinct variant lines.");
        SupplierId = supplierId; Lines = copy; CreatedAt = now;
    }
    public void Submit(long expectedRevision, DateTimeOffset now)
    { Expected(expectedRevision); if (Status != PurchaseOrderStatus.Draft) throw new ProcurementConflictException("Only a draft purchase order can be submitted."); Status = PurchaseOrderStatus.Ordered; SubmittedAt = now; Advance(); }
    public void Cancel(long expectedRevision, DateTimeOffset now)
    { Expected(expectedRevision); if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Ordered) || Receipts.Count != 0) throw new ProcurementConflictException("Only a draft or unreceived ordered purchase order can be cancelled."); Status = PurchaseOrderStatus.Cancelled; CancelledAt = now; Advance(); }
    public void Receive(long expectedRevision)
    {
        Expected(expectedRevision);
        if (Status is not (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived)) throw new ProcurementConflictException("Only a submitted purchase order can receive stock.");
        Status = Lines.All(l => l.ReceivedQuantity == l.OrderedQuantity) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
        Advance();
    }
    public void Expected(long expected) { if (expected != Revision) throw new ProcurementConflictException("The purchase order changed. Reload its revision."); }
    private void Advance() { Revision = checked(Revision + 1); }
}

public sealed class PurchaseOrderLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PurchaseOrderId { get; private set; }
    public Guid? ProductVariantId { get; private set; }
    public string Sku { get; private set; } = "";
    public string VariantName { get; private set; } = "";
    public int OrderedQuantity { get; private set; }
    public int ReceivedQuantity { get; private set; }
    private PurchaseOrderLine() { }
    public PurchaseOrderLine(Guid variantId, string sku, string name, int quantity)
    { var cleanSku=sku.Trim();var cleanName=name.Trim();if (variantId == Guid.Empty || quantity is < 1 or > 1_000_000 || cleanSku.Length is <1 or >64 || cleanName.Length is <1 or >120) throw new DomainException("Each purchase-order line needs a valid variant snapshot and quantity from 1 to 1,000,000."); ProductVariantId = variantId; Sku = cleanSku; VariantName = cleanName; OrderedQuantity = quantity; }
    public void AddReceived(int quantity)
    { if (quantity <= 0 || (long)ReceivedQuantity + quantity > OrderedQuantity) throw new InvalidProcurementException("A receipt quantity must be positive and cannot exceed the ordered remainder."); ReceivedQuantity = checked(ReceivedQuantity + quantity); }
}

public sealed record PurchaseOrderReceiptChange(Guid PurchaseOrderLineId, int Quantity);
public sealed class PurchaseOrderReceipt
{
    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public List<PurchaseOrderReceiptLine> Lines { get; private set; } = [];
    private PurchaseOrderReceipt() { }
    public PurchaseOrderReceipt(Guid operationId, Guid poId, Guid actorId, long expectedRevision, DateTimeOffset now, IEnumerable<PurchaseOrderReceiptLine> lines)
    { if (operationId == Guid.Empty || poId==Guid.Empty || actorId==Guid.Empty) throw new DomainException("A receipt requires a nonempty operation, purchase order, and actor ID."); Id = operationId; PurchaseOrderId = poId; ActorId = actorId; ReceivedAt = now; Lines = lines.ToList(); if(Lines.Count is <1 or >100)throw new DomainException("A receipt needs 1–100 lines."); Fingerprint = ComputeFingerprint(poId, expectedRevision, Lines.Select(x => new PurchaseOrderReceiptChange(x.PurchaseOrderLineId, x.Quantity))); }
    public static string ComputeFingerprint(Guid poId, long expectedRevision, IEnumerable<PurchaseOrderReceiptChange> changes)
    { var ordered=changes.OrderBy(x=>x.PurchaseOrderLineId).ToArray(); var bytes=JsonSerializer.SerializeToUtf8Bytes(new { purchaseOrderId=poId, expectedRevision, lines=ordered.Select(x=>new { lineId=x.PurchaseOrderLineId, quantity=x.Quantity }) }); return Convert.ToHexString(SHA256.HashData(bytes)); }
}
public sealed class PurchaseOrderReceiptLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ReceiptId { get; private set; }
    public Guid PurchaseOrderLineId { get; private set; }
    public Guid? ProductVariantId { get; private set; }
    public string Sku { get; private set; } = "";
    public int Quantity { get; private set; }
    public int BeforeOnHand { get; private set; }
    public int AfterOnHand { get; private set; }
    private PurchaseOrderReceiptLine() { }
    public PurchaseOrderReceiptLine(Guid lineId, Guid? variantId, string sku, int quantity, int before, int after)
    { PurchaseOrderLineId=lineId; ProductVariantId=variantId; Sku=sku; Quantity=quantity; BeforeOnHand=before; AfterOnHand=after; }
}

public sealed class ProcurementConflictException(string message) : DomainException(message);
public sealed class InvalidProcurementException(string message) : DomainException(message);
