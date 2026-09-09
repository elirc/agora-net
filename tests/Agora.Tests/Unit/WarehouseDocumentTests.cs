using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public sealed class WarehouseDocumentTests
{
    [Fact]
    public void Purchase_order_and_count_session_enforce_collection_caps()
    {
        var orderLines = Enumerable.Range(0, 101)
            .Select(index => new PurchaseOrderLine(Guid.NewGuid(), $"SKU-{index}", $"Variant {index}", 1))
            .ToArray();
        Assert.Throws<DomainException>(() =>
            new PurchaseOrder(Guid.NewGuid(), orderLines, DateTimeOffset.UnixEpoch));

        var countLines = Enumerable.Range(0, 101)
            .Select(index => new InventoryCountLine(Guid.NewGuid(), $"SKU-{index}", 1, 0, 0))
            .ToArray();
        Assert.Throws<DomainException>(() =>
            new InventoryCountSession(Guid.NewGuid(), DateTimeOffset.UnixEpoch, countLines));
    }

    [Fact]
    public void Purchase_order_tracks_partial_and_complete_receipts()
    {
        var a=new PurchaseOrderLine(Guid.NewGuid(),"A","Alpha",10);var b=new PurchaseOrderLine(Guid.NewGuid(),"B","Beta",5);
        var po=new PurchaseOrder(Guid.NewGuid(),[a,b],DateTimeOffset.UnixEpoch);
        po.Submit(0,DateTimeOffset.UnixEpoch.AddHours(1));
        a.AddReceived(4);po.Receive(1);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived,po.Status);Assert.Equal(4,a.ReceivedQuantity);Assert.Equal(2,po.Revision);
        a.AddReceived(6);b.AddReceived(5);po.Receive(2);
        Assert.Equal(PurchaseOrderStatus.Received,po.Status);Assert.Equal(3,po.Revision);
    }

    [Fact]
    public void Purchase_order_rejects_over_receipt_and_cancel_after_receipt()
    {
        var line=new PurchaseOrderLine(Guid.NewGuid(),"A","Alpha",3);var po=new PurchaseOrder(Guid.NewGuid(),[line],DateTimeOffset.UnixEpoch);po.Submit(0,DateTimeOffset.UnixEpoch);
        Assert.Throws<InvalidProcurementException>(()=>line.AddReceived(4));
        line.AddReceived(1);po.Receive(1);
        Assert.Throws<ProcurementConflictException>(()=>po.Cancel(2,DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Receipt_fingerprint_is_order_independent_but_revision_sensitive()
    {
        var po=Guid.NewGuid();var a=new PurchaseOrderReceiptChange(Guid.NewGuid(),2);var b=new PurchaseOrderReceiptChange(Guid.NewGuid(),4);
        Assert.Equal(PurchaseOrderReceipt.ComputeFingerprint(po,3,[a,b]),PurchaseOrderReceipt.ComputeFingerprint(po,3,[b,a]));
        Assert.NotEqual(PurchaseOrderReceipt.ComputeFingerprint(po,3,[a,b]),PurchaseOrderReceipt.ComputeFingerprint(po,4,[a,b]));
    }

    [Fact]
    public void Count_entry_advances_revision_without_changing_baseline()
    {
        var line=new InventoryCountLine(Guid.NewGuid(),"A",10,2,7);var session=new InventoryCountSession(Guid.NewGuid(),DateTimeOffset.UnixEpoch,[line]);
        session.Record(line.Id,9,0);
        Assert.Equal(1,session.Revision);Assert.Equal(9,line.CountedQuantity);Assert.Equal(10,line.BaselineOnHand);Assert.Equal(7,line.BaselineVersion);
        Assert.Throws<InventoryCountConflictException>(()=>session.Record(line.Id,8,0));
    }

    [Fact]
    public void Count_requires_every_line_and_terminal_states_are_immutable()
    {
        var actor=Guid.NewGuid();var a=new InventoryCountLine(Guid.NewGuid(),"A",10,2,0);var b=new InventoryCountLine(Guid.NewGuid(),"B",5,0,0);var session=new InventoryCountSession(actor,DateTimeOffset.UnixEpoch,[a,b]);
        session.Record(a.Id,9,0);
        Assert.Throws<InvalidInventoryCountException>(()=>session.Apply(actor,DateTimeOffset.UnixEpoch,1));
        session.Record(b.Id,5,1);session.Apply(actor,DateTimeOffset.UnixEpoch,2);
        Assert.Equal(InventoryCountStatus.Applied,session.Status);Assert.Throws<InventoryCountConflictException>(()=>session.Cancel(actor,DateTimeOffset.UnixEpoch,3));
    }
}
