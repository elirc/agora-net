using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record CreateSupplierRequest([Required,MaxLength(120)]string Name,[MaxLength(120)]string? Reference);
public sealed record SupplierResponse(Guid Id,string Name,string? Reference,bool IsActive,DateTimeOffset CreatedAt)
{ public static SupplierResponse From(Supplier x)=>new(x.Id,x.Name,x.Reference,x.IsActive,x.CreatedAt); }
public sealed record PurchaseOrderLineRequest(Guid VariantId,[Range(1,1_000_000)]int Quantity);
public sealed record CreatePurchaseOrderRequest(
    Guid SupplierId,
    [Required, MinLength(1), MaxLength(100)] List<PurchaseOrderLineRequest> Lines) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Lines is not null && Lines.Any(line => line is null))
            yield return new ValidationResult("Purchase-order lines cannot be null.", [nameof(Lines)]);
    }
}
public sealed record RevisionRequest([Required,Range(0,long.MaxValue)]long? ExpectedRevision);
public sealed record ReceivePurchaseOrderLineRequest(Guid PurchaseOrderLineId,[Range(1,1_000_000)]int Quantity);
public sealed record ReceivePurchaseOrderRequest(
    Guid OperationId,
    [Required, Range(0, long.MaxValue)] long? ExpectedRevision,
    [Required, MinLength(1), MaxLength(100)] List<ReceivePurchaseOrderLineRequest> Lines) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Lines is not null && Lines.Any(line => line is null))
            yield return new ValidationResult("Receipt lines cannot be null.", [nameof(Lines)]);
    }
}
public sealed record PurchaseOrderLineResponse(Guid Id,Guid? VariantId,string Sku,string VariantName,int OrderedQuantity,int ReceivedQuantity,int RemainingQuantity);
public sealed record PurchaseOrderReceiptLineResponse(Guid PurchaseOrderLineId,Guid? VariantId,string Sku,int Quantity,int BeforeOnHand,int AfterOnHand);
public sealed record PurchaseOrderReceiptResponse(Guid OperationId,Guid PurchaseOrderId,Guid ActorId,DateTimeOffset ReceivedAt,IReadOnlyList<PurchaseOrderReceiptLineResponse> Lines)
{ public static PurchaseOrderReceiptResponse From(PurchaseOrderReceipt x)=>new(x.Id,x.PurchaseOrderId,x.ActorId,x.ReceivedAt,x.Lines.OrderBy(l=>l.PurchaseOrderLineId).Select(l=>new PurchaseOrderReceiptLineResponse(l.PurchaseOrderLineId,l.ProductVariantId,l.Sku,l.Quantity,l.BeforeOnHand,l.AfterOnHand)).ToArray()); }
public sealed record PurchaseOrderResponse(Guid Id,SupplierResponse Supplier,string Status,long Revision,DateTimeOffset CreatedAt,DateTimeOffset? SubmittedAt,DateTimeOffset? CancelledAt,IReadOnlyList<PurchaseOrderLineResponse> Lines,IReadOnlyList<PurchaseOrderReceiptResponse> Receipts)
{ public static PurchaseOrderResponse From(PurchaseOrder x)=>new(x.Id,SupplierResponse.From(x.Supplier!),x.Status.ToString(),x.Revision,x.CreatedAt,x.SubmittedAt,x.CancelledAt,x.Lines.OrderBy(l=>l.Id).Select(l=>new PurchaseOrderLineResponse(l.Id,l.ProductVariantId,l.Sku,l.VariantName,l.OrderedQuantity,l.ReceivedQuantity,l.OrderedQuantity-l.ReceivedQuantity)).ToArray(),x.Receipts.OrderBy(r=>r.ReceivedAt).ThenBy(r=>r.Id).Select(PurchaseOrderReceiptResponse.From).ToArray()); }

public sealed record CreateInventoryCountRequest([Required,MinLength(1),MaxLength(100)]List<Guid> VariantIds);
public sealed record RecordInventoryCountRequest([Range(0,1_000_000)]int CountedQuantity,[Required,Range(0,long.MaxValue)]long? ExpectedRevision);
public sealed record InventoryCountLineResponse(Guid Id,Guid? VariantId,string Sku,int BaselineOnHand,int BaselineReserved,int BaselineVersion,int? CountedQuantity,int? AppliedOnHand,int? Difference);
public sealed record InventoryCountResponse(Guid Id,string Status,long Revision,Guid CreatedBy,DateTimeOffset CreatedAt,Guid? AppliedBy,DateTimeOffset? AppliedAt,Guid? CancelledBy,DateTimeOffset? CancelledAt,IReadOnlyList<InventoryCountLineResponse> Lines)
{ public static InventoryCountResponse From(InventoryCountSession x)=>new(x.Id,x.Status.ToString(),x.Revision,x.CreatedBy,x.CreatedAt,x.AppliedBy,x.AppliedAt,x.CancelledBy,x.CancelledAt,x.Lines.OrderBy(l=>l.Sku,StringComparer.Ordinal).ThenBy(l=>l.Id).Select(l=>new InventoryCountLineResponse(l.Id,l.ProductVariantId,l.Sku,l.BaselineOnHand,l.BaselineReserved,l.BaselineVersion,l.CountedQuantity,l.AppliedOnHand,l.Difference)).ToArray()); }
