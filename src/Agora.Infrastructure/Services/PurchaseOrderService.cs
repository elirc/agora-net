using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record PurchaseOrderReceiptResult(PurchaseOrderReceipt Receipt, bool Replayed);

public sealed class PurchaseOrderService(AgoraDbContext db, TimeProvider clock)
{
    public async Task<Supplier> CreateSupplierAsync(string name,string? reference,CancellationToken ct)
    { var entity=new Supplier(name,reference,clock.GetUtcNow()); db.Set<Supplier>().Add(entity); await db.SaveChangesAsync(ct); return entity; }
    public async Task<Supplier> DeactivateSupplierAsync(Guid id,CancellationToken ct)
    { var supplier=await db.Set<Supplier>().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("Supplier was not found."); supplier.Deactivate(); await db.SaveChangesAsync(ct); return supplier; }
    public async Task<PurchaseOrder> CreateAsync(Guid supplierId,IReadOnlyList<(Guid VariantId,int Quantity)> requested,CancellationToken ct)
    {
        if(requested.Count is <1 or >100 || requested.Any(x=>x.VariantId==Guid.Empty || x.Quantity is <1 or >1_000_000) || requested.Select(x=>x.VariantId).Distinct().Count()!=requested.Count) throw new InvalidProcurementException("Supply 1–100 distinct valid purchase-order lines.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var supplier=await db.Set<Supplier>().SingleOrDefaultAsync(x=>x.Id==supplierId,ct)??throw new NotFoundException("Supplier was not found.");
        supplier.AcceptNewPurchaseOrder();
        var ids=requested.Select(x=>x.VariantId).ToArray();
        var variants=await db.ProductVariants.AsNoTracking().Where(x=>ids.Contains(x.Id) && x.Product!.IsActive).Select(x=>new{x.Id,x.Sku,x.Name}).ToDictionaryAsync(x=>x.Id,ct);
        if(variants.Count!=ids.Length) throw new InvalidProcurementException("Every line must reference a current variant on an active product.");
        var po=new PurchaseOrder(supplierId,requested.Select(x=>new PurchaseOrderLine(x.VariantId,variants[x.VariantId].Sku,variants[x.VariantId].Name,x.Quantity)),clock.GetUtcNow());
        db.Set<PurchaseOrder>().Add(po); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return po;
    }
    public async Task<PurchaseOrder> SubmitAsync(Guid id,long revision,CancellationToken ct)=>await Transition(id,revision,true,ct);
    public async Task<PurchaseOrder> CancelAsync(Guid id,long revision,CancellationToken ct)=>await Transition(id,revision,false,ct);
    private async Task<PurchaseOrder> Transition(Guid id,long revision,bool submit,CancellationToken ct)
    { await using var tx=await db.Database.BeginTransactionAsync(ct); var po=await Query(db).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("Purchase order was not found."); if(submit)po.Submit(revision,clock.GetUtcNow());else po.Cancel(revision,clock.GetUtcNow()); await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return po; }

    public async Task<PurchaseOrderReceiptResult> ReceiveAsync(Guid poId,Guid operationId,long expectedRevision,Guid actor,IReadOnlyList<PurchaseOrderReceiptChange> changes,CancellationToken ct)
    {
        if(actor==Guid.Empty || operationId==Guid.Empty || changes.Count is <1 or >100 || changes.Any(x=>x.PurchaseOrderLineId==Guid.Empty||x.Quantity<=0) || changes.Select(x=>x.PurchaseOrderLineId).Distinct().Count()!=changes.Count) throw new InvalidProcurementException("A receipt requires an actor, operation ID, and distinct positive line quantities.");
        var fingerprint=PurchaseOrderReceipt.ComputeFingerprint(poId,expectedRevision,changes);
        var old=await ReceiptQuery().SingleOrDefaultAsync(x=>x.Id==operationId,ct);
        if(old is not null)return Replay(old,poId,fingerprint);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        old=await ReceiptQuery().SingleOrDefaultAsync(x=>x.Id==operationId,ct); if(old is not null){await tx.CommitAsync(ct);return Replay(old,poId,fingerprint);}
        var po=await Query(db).SingleOrDefaultAsync(x=>x.Id==poId,ct)??throw new NotFoundException("Purchase order was not found.");
        po.Expected(expectedRevision);
        if(po.Status is not(PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived))throw new ProcurementConflictException("Only a submitted purchase order can receive stock.");
        var lineMap=po.Lines.ToDictionary(x=>x.Id); if(changes.Any(x=>!lineMap.ContainsKey(x.PurchaseOrderLineId)))throw new InvalidProcurementException("Every receipt line must belong to this purchase order.");
        var variantIds=changes.Select(x=>lineMap[x.PurchaseOrderLineId].ProductVariantId).ToArray(); if(variantIds.Any(x=>x is null))throw new InvalidProcurementException("A deleted purchase-order variant cannot be received.");
        var ids=variantIds.Select(x=>x!.Value).ToArray(); var stocks=await db.InventoryItems.Where(x=>ids.Contains(x.ProductVariantId)).ToDictionaryAsync(x=>x.ProductVariantId,ct); if(stocks.Count!=ids.Length)throw new InvalidProcurementException("Every received variant needs an inventory record.");
        foreach(var change in changes){var line=lineMap[change.PurchaseOrderLineId]; if((long)line.ReceivedQuantity+change.Quantity>line.OrderedQuantity)throw new InvalidProcurementException("A receipt cannot exceed an ordered remainder."); var stock=stocks[line.ProductVariantId!.Value]; if((long)stock.QuantityOnHand+change.Quantity>int.MaxValue || stock.Version==int.MaxValue)throw new InvalidProcurementException("The stock quantity or revision cannot be incremented safely.");}
        var receiptLines=new List<PurchaseOrderReceiptLine>();
        foreach(var change in changes){var line=lineMap[change.PurchaseOrderLineId];var stock=stocks[line.ProductVariantId!.Value];var before=stock.QuantityOnHand;line.AddReceived(change.Quantity);stock.Restock(change.Quantity);receiptLines.Add(new PurchaseOrderReceiptLine(line.Id,line.ProductVariantId,line.Sku,change.Quantity,before,stock.QuantityOnHand));}
        po.Receive(expectedRevision); var receipt=new PurchaseOrderReceipt(operationId,poId,actor,expectedRevision,clock.GetUtcNow(),receiptLines);db.Set<PurchaseOrderReceipt>().Add(receipt);
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(receipt,false); }
        catch(DbUpdateException error) when(error.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); var winner=await ReceiptQuery().SingleOrDefaultAsync(x=>x.Id==operationId,ct); if(winner is null)throw new ProcurementConflictException("A competing warehouse write conflicted with this receipt."); return Replay(winner,poId,fingerprint); }
    }
    public Task<PurchaseOrder?> ReadAsync(Guid id,CancellationToken ct)=>Query(db).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
    private IQueryable<PurchaseOrderReceipt> ReceiptQuery()=>db.Set<PurchaseOrderReceipt>().AsNoTracking().Include(x=>x.Lines);
    private static IQueryable<PurchaseOrder> Query(AgoraDbContext context)=>context.Set<PurchaseOrder>().Include(x=>x.Supplier).Include(x=>x.Lines).Include(x=>x.Receipts).ThenInclude(x=>x.Lines);
    private static PurchaseOrderReceiptResult Replay(PurchaseOrderReceipt receipt,Guid poId,string fingerprint){if(receipt.PurchaseOrderId!=poId||receipt.Fingerprint!=fingerprint)throw new ProcurementConflictException("This operation ID was already used with different normalized content.");return new(receipt,true);}
}
