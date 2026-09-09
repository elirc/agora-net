using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record AssignmentRelease(Guid OrderId,Guid AssignmentId,Guid OwnerId,DateTimeOffset ReleasedAt);

public sealed class OrderHoldService(AgoraDbContext db,TimeProvider clock)
{
    public async Task<OrderHold> CreateAsync(string number,OrderHoldReason reason,string? note,Guid actor,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var order=await db.Orders.SingleOrDefaultAsync(x=>x.Number==number,ct)??throw new NotFoundException("Order was not found.");
        if(order.Status is not(OrderStatus.Paid or OrderStatus.PartiallyFulfilled))throw new WarehouseCoordinationConflictException("Only a paid or partially fulfilled order can be held.");
        if(await db.Set<OrderHold>().AnyAsync(x=>x.OrderId==order.Id&&x.IsActive,ct))throw new WarehouseCoordinationConflictException("The order already has an active hold.");
        var hold=new OrderHold(order.Id,reason,note,actor,clock.GetUtcNow());db.Set<OrderHold>().Add(hold);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        { throw new WarehouseCoordinationConflictException("The order already has an active hold."); }
        await tx.CommitAsync(ct);return hold;
    }
    public async Task<OrderHold> ReleaseAsync(string number,Guid holdId,long revision,Guid actor,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var hold=await db.Set<OrderHold>().SingleOrDefaultAsync(x=>x.Id==holdId&&x.OrderId==db.Orders.Where(o=>o.Number==number).Select(o=>o.Id).FirstOrDefault(),ct)??throw new NotFoundException("Hold was not found for this order.");
        hold.Release(actor,revision,clock.GetUtcNow());await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return hold;
    }
    public async Task<IReadOnlyList<OrderHold>> ListAsync(string number,CancellationToken ct)
    {var orderId=await db.Orders.Where(x=>x.Number==number).Select(x=>(Guid?)x.Id).SingleOrDefaultAsync(ct)??throw new NotFoundException("Order was not found.");return await db.Set<OrderHold>().AsNoTracking().Where(x=>x.OrderId==orderId).OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).ToArrayAsync(ct);}
}

public sealed class WarehouseAssignmentService(AgoraDbContext db,TimeProvider clock)
{
    public async Task<WarehouseAssignment> ClaimAsync(string number,Guid actor,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);var now=clock.GetUtcNow();
        var order=await db.Orders.SingleOrDefaultAsync(x=>x.Number==number,ct)??throw new NotFoundException("Order was not found.");
        if(order.Status is not(OrderStatus.Paid or OrderStatus.PartiallyFulfilled))throw new WarehouseCoordinationConflictException("Only a paid or partially fulfilled order can be assigned.");
        var slot=await db.Set<WarehouseAssignment>().SingleOrDefaultAsync(x=>x.OrderId==order.Id,ct);
        if(slot is null){slot=new WarehouseAssignment(order.Id,actor,now);db.Set<WarehouseAssignment>().Add(slot);}else{if(slot.IsLive(now))throw new WarehouseCoordinationConflictException("Another administrator has a live assignment.");slot.Replace(actor,now);}
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        { throw new WarehouseCoordinationConflictException("Another administrator won the assignment claim."); }
        await tx.CommitAsync(ct);return slot;
    }
    public async Task<WarehouseAssignment?> ReadAsync(string number,CancellationToken ct)
    {var orderId=await db.Orders.Where(x=>x.Number==number).Select(x=>(Guid?)x.Id).SingleOrDefaultAsync(ct)??throw new NotFoundException("Order was not found.");return await db.Set<WarehouseAssignment>().AsNoTracking().SingleOrDefaultAsync(x=>x.OrderId==orderId,ct);}
    public async Task<WarehouseAssignment> RenewAsync(string number,Guid assignmentId,long revision,Guid actor,CancellationToken ct)
    {await using var tx=await db.Database.BeginTransactionAsync(ct);var slot=await Owned(number,ct);slot.Renew(actor,assignmentId,revision,clock.GetUtcNow());await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return slot;}
    public async Task<AssignmentRelease> ReleaseAsync(string number,Guid assignmentId,long revision,Guid actor,CancellationToken ct)
    {await using var tx=await db.Database.BeginTransactionAsync(ct);var slot=await Owned(number,ct);var now=clock.GetUtcNow();slot.Authorize(actor,assignmentId,revision,now);var receipt=new AssignmentRelease(slot.OrderId,slot.AssignmentId,slot.OwnerId,now);db.Remove(slot);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return receipt;}
    private async Task<WarehouseAssignment> Owned(string number,CancellationToken ct)
    {var orderId=await db.Orders.Where(x=>x.Number==number).Select(x=>(Guid?)x.Id).SingleOrDefaultAsync(ct)??throw new NotFoundException("Order was not found.");return await db.Set<WarehouseAssignment>().SingleOrDefaultAsync(x=>x.OrderId==orderId,ct)??throw new NotFoundException("Warehouse assignment was not found.");}
}
