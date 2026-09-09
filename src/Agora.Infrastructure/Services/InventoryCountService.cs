using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed class InventoryCountService(AgoraDbContext db,TimeProvider clock)
{
    public async Task<InventoryCountSession> CreateAsync(Guid actor,IReadOnlyList<Guid> variantIds,CancellationToken ct)
    {
        if(actor==Guid.Empty||variantIds.Count is<1 or>100||variantIds.Contains(Guid.Empty)||variantIds.Distinct().Count()!=variantIds.Count)throw new InvalidInventoryCountException("Select 1–100 distinct variants.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var ids=variantIds.ToArray();var rows=await db.InventoryItems.AsNoTracking().Where(x=>ids.Contains(x.ProductVariantId)).Select(x=>new{x.ProductVariantId,x.ProductVariant!.Sku,x.QuantityOnHand,x.QuantityReserved,x.Version}).ToDictionaryAsync(x=>x.ProductVariantId,ct);
        if(rows.Count!=ids.Length)throw new InvalidInventoryCountException("Every selected variant needs a current inventory row.");
        var session=new InventoryCountSession(actor,clock.GetUtcNow(),ids.Select(id=>{var x=rows[id];return new InventoryCountLine(id,x.Sku,x.QuantityOnHand,x.QuantityReserved,x.Version);}));db.Set<InventoryCountSession>().Add(session);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return session;
    }
    public async Task<InventoryCountSession> RecordAsync(Guid id,Guid lineId,int count,long revision,CancellationToken ct)
    {await using var tx=await db.Database.BeginTransactionAsync(ct);var session=await Query(false).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("Count session was not found.");session.Record(lineId,count,revision);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return session;}
    public async Task<InventoryCountSession> CancelAsync(Guid id,Guid actor,long revision,CancellationToken ct)
    {await using var tx=await db.Database.BeginTransactionAsync(ct);var session=await Query(false).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("Count session was not found.");session.Cancel(actor,clock.GetUtcNow(),revision);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return session;}
    public async Task<InventoryCountSession> ApplyAsync(Guid id,Guid actor,long revision,CancellationToken ct)
    {
        var snapshot=await Query(true).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new NotFoundException("Count session was not found.");if(snapshot.Status==InventoryCountStatus.Applied)return snapshot;
        await using var tx=await db.Database.BeginTransactionAsync(ct);var session=await Query(false).SingleAsync(x=>x.Id==id,ct);if(session.Status==InventoryCountStatus.Applied){await tx.CommitAsync(ct);return session;}session.Expected(revision);if(session.Status!=InventoryCountStatus.Open)throw new InventoryCountConflictException("Only an open session can be applied.");if(session.Lines.Any(x=>x.CountedQuantity is null))throw new InvalidInventoryCountException("Every line must be counted before applying the session.");
        var ids = session.Lines
            .Where(line => line.ProductVariantId.HasValue)
            .Select(line => line.ProductVariantId!.Value)
            .ToArray();
        var stocks = await db.InventoryItems
            .Where(stock => ids.Contains(stock.ProductVariantId))
            .ToDictionaryAsync(stock => stock.ProductVariantId, ct);
        var problems = new List<string>();
        foreach(var line in session.Lines){if(line.ProductVariantId is null||!stocks.TryGetValue(line.ProductVariantId.Value,out var stock)){problems.Add($"{line.Sku}: inventory missing");continue;}if(stock.Version!=line.BaselineVersion)problems.Add($"{line.Sku}: inventory version changed");if(line.CountedQuantity<stock.QuantityReserved)problems.Add($"{line.Sku}: count is below reserved stock");if(stock.Version==int.MaxValue)problems.Add($"{line.Sku}: inventory revision is exhausted");}
        if(problems.Count>0)throw new InventoryCountConflictException("The count cannot be applied: "+string.Join("; ",problems));
        foreach(var line in session.Lines){var stock=stocks[line.ProductVariantId!.Value];stock.SetStock(line.CountedQuantity!.Value);line.RecordApplication(stock.QuantityOnHand);}session.Apply(actor,clock.GetUtcNow(),revision);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return session;
    }
    public Task<InventoryCountSession?> ReadAsync(Guid id,CancellationToken ct)=>Query(true).SingleOrDefaultAsync(x=>x.Id==id,ct);
    private IQueryable<InventoryCountSession> Query(bool noTracking){var q=db.Set<InventoryCountSession>().Include(x=>x.Lines).AsQueryable();return noTracking?q.AsNoTracking():q;}
}
