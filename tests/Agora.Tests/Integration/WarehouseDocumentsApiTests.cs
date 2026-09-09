using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public sealed class WarehouseDocumentsApiTests(AgoraApiFactory factory):IClassFixture<AgoraApiFactory>,IAsyncLifetime
{
    private readonly HttpClient client=factory.CreateClient();
    public Task InitializeAsync()=>client.AuthenticateAsAdminAsync(); public Task DisposeAsync()=>Task.CompletedTask;

    [Fact]
    public async Task Inactive_supplier_blocks_new_order_but_submitted_order_remains_receivable()
    {
        Guid variant = default;
        await factory.WithDbAsync(async db =>
            variant = await db.ProductVariants.Select(x => x.Id).FirstAsync());

        var supplierResult = await client.PostAsJsonAsync(
            "/api/admin/suppliers", new CreateSupplierRequest("Lifecycle supplier", null));
        var supplier = (await supplierResult.Content.ReadFromJsonAsync<SupplierResponse>())!;
        var orderResult = await client.PostAsJsonAsync(
            "/api/admin/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, [new(variant, 1)]));
        var order = (await orderResult.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        order = (await (await client.PostAsJsonAsync(
            $"/api/admin/purchase-orders/{order.Id}/submit", new RevisionRequest(0)))
            .Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;

        (await client.PostAsync($"/api/admin/suppliers/{supplier.Id}/deactivate", null))
            .EnsureSuccessStatusCode();
        var rejected = await client.PostAsJsonAsync(
            "/api/admin/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, [new(variant, 1)]));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        var receipt = await client.PostAsJsonAsync(
            $"/api/admin/purchase-orders/{order.Id}/receipts",
            new ReceivePurchaseOrderRequest(Guid.NewGuid(), order.Revision,
                [new(order.Lines.Single().Id, 1)]));
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
    }

    [Fact]
    public async Task Partial_receipt_replays_once_and_final_receipt_completes_order()
    {
        Guid a=default,b=default;int beforeA=0;
        await factory.WithDbAsync(async db=>{var rows=await db.ProductVariants.Include(x=>x.Inventory).Where(x=>x.Inventory!=null).Take(2).ToArrayAsync();a=rows[0].Id;b=rows[1].Id;beforeA=rows[0].Inventory!.QuantityOnHand;});
        var supplierResponse=await client.PostAsJsonAsync("/api/admin/suppliers",new CreateSupplierRequest("Learning Supply",null));supplierResponse.EnsureSuccessStatusCode();var supplier=(await supplierResponse.Content.ReadFromJsonAsync<SupplierResponse>())!;
        var create=await client.PostAsJsonAsync("/api/admin/purchase-orders",new CreatePurchaseOrderRequest(supplier.Id,[new(a,10),new(b,5)]));create.EnsureSuccessStatusCode();var po=(await create.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        var submit=await client.PostAsJsonAsync($"/api/admin/purchase-orders/{po.Id}/submit",new RevisionRequest(po.Revision));po=(await submit.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        var lineA=po.Lines.Single(x=>x.VariantId==a);var lineB=po.Lines.Single(x=>x.VariantId==b);
        var operation=Guid.NewGuid();var first=new ReceivePurchaseOrderRequest(operation,po.Revision,[new(lineA.Id,4)]);var receipt=await client.PostAsJsonAsync($"/api/admin/purchase-orders/{po.Id}/receipts",first);Assert.Equal(HttpStatusCode.Created,receipt.StatusCode);
        var replay=await client.PostAsJsonAsync($"/api/admin/purchase-orders/{po.Id}/receipts",first);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        po=await client.GetFromJsonAsync<PurchaseOrderResponse>($"/api/admin/purchase-orders/{po.Id}");Assert.Equal("PartiallyReceived",po!.Status);
        var final=await client.PostAsJsonAsync($"/api/admin/purchase-orders/{po.Id}/receipts",new ReceivePurchaseOrderRequest(Guid.NewGuid(),po.Revision,[new(lineA.Id,6),new(lineB.Id,5)]));final.EnsureSuccessStatusCode();
        po=await client.GetFromJsonAsync<PurchaseOrderResponse>($"/api/admin/purchase-orders/{po.Id}");Assert.Equal("Received",po!.Status);
        await factory.WithDbAsync(async db=>Assert.Equal(beforeA+10,(await db.InventoryItems.SingleAsync(x=>x.ProductVariantId==a)).QuantityOnHand));
    }

    [Fact]
    public async Task Count_stages_values_then_applies_atomically_and_replays()
    {
        Guid variant=default;int baseline=0,reserved=0;
        await factory.WithDbAsync(async db=>{var x=await db.InventoryItems.OrderBy(x=>x.Id).FirstAsync();variant=x.ProductVariantId;baseline=x.QuantityOnHand;reserved=x.QuantityReserved;});
        var createdResponse=await client.PostAsJsonAsync("/api/admin/inventory-counts",new CreateInventoryCountRequest([variant]));createdResponse.EnsureSuccessStatusCode();var count=(await createdResponse.Content.ReadFromJsonAsync<InventoryCountResponse>())!;var target=Math.Max(reserved,baseline-1);
        var edit=await client.PutAsJsonAsync($"/api/admin/inventory-counts/{count.Id}/lines/{count.Lines[0].Id}",new RecordInventoryCountRequest(target,count.Revision));count=(await edit.Content.ReadFromJsonAsync<InventoryCountResponse>())!;
        var apply=await client.PostAsJsonAsync($"/api/admin/inventory-counts/{count.Id}/apply",new RevisionRequest(count.Revision));apply.EnsureSuccessStatusCode();var applied=(await apply.Content.ReadFromJsonAsync<InventoryCountResponse>())!;Assert.Equal("Applied",applied.Status);Assert.Equal(target-baseline,applied.Lines[0].Difference);
        var replay=await client.PostAsJsonAsync($"/api/admin/inventory-counts/{count.Id}/apply",new RevisionRequest(0));Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        await factory.WithDbAsync(async db=>Assert.Equal(target,(await db.InventoryItems.SingleAsync(x=>x.ProductVariantId==variant)).QuantityOnHand));
    }

    [Fact]
    public async Task Stale_count_rejects_whole_application()
    {
        Guid[] ids=[];int[] before=[];
        await factory.WithDbAsync(async db=>{var rows=await db.InventoryItems.OrderBy(x=>x.Id).Take(2).ToArrayAsync();ids=rows.Select(x=>x.ProductVariantId).ToArray();before=rows.Select(x=>x.QuantityOnHand).ToArray();});
        var response=await client.PostAsJsonAsync("/api/admin/inventory-counts",new CreateInventoryCountRequest(ids.ToList()));var count=(await response.Content.ReadFromJsonAsync<InventoryCountResponse>())!;
        foreach(var line in count.Lines){var edit=await client.PutAsJsonAsync($"/api/admin/inventory-counts/{count.Id}/lines/{line.Id}",new RecordInventoryCountRequest(line.BaselineOnHand,count.Revision));count=(await edit.Content.ReadFromJsonAsync<InventoryCountResponse>())!;}
        await factory.WithDbAsync(async db=>{var stock=await db.InventoryItems.SingleAsync(x=>x.ProductVariantId==ids[0]);stock.Restock(1);await db.SaveChangesAsync();});
        var apply=await client.PostAsJsonAsync($"/api/admin/inventory-counts/{count.Id}/apply",new RevisionRequest(count.Revision));Assert.Equal(HttpStatusCode.Conflict,apply.StatusCode);
        await factory.WithDbAsync(async db=>{var values=await db.InventoryItems.Where(x=>ids.Contains(x.ProductVariantId)).OrderBy(x=>x.Id).Select(x=>x.QuantityOnHand).ToArrayAsync();Assert.Contains(before[0]+1,values);Assert.Contains(before[1],values);});
    }
}
