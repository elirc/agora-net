using System.Text.Json;
using Agora.Domain.Common;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed class AccountExportTooLargeException(string message) : DomainException(message);
public sealed record ExportProfile(Guid Id,string Email,string FullName,string Role,DateTimeOffset CreatedAt);
public sealed record ExportAddress(Guid Id,string Label,bool IsDefault,string FullName,string Line1,string? Line2,string City,string Region,string PostalCode,string Country,DateTimeOffset CreatedAt);
public sealed record ExportOrder(Guid Id,string Number,string Status,string Currency,decimal Subtotal,decimal DiscountAmount,decimal TaxAmount,decimal ShippingAmount,decimal Total,DateTimeOffset CreatedAt,DateTimeOffset? PaidAt,DateTimeOffset? FulfilledAt);
public sealed record ExportOrderItem(Guid Id,Guid OrderId,string Sku,string ProductName,string VariantName,decimal UnitPrice,int Quantity,decimal LineTotal);
public sealed record ExportFulfillment(Guid Id,Guid OrderId,string Number,string Carrier,string? TrackingNumber,string TrackingStatus,DateTimeOffset CreatedAt);
public sealed record ExportFulfillmentItem(Guid Id,Guid FulfillmentId,Guid OrderItemId,string Sku,int Quantity);
public sealed record ExportReturn(Guid Id,Guid OrderId,string Number,string Status,string Reason,string Comment,decimal RefundAmount,string Currency,DateTimeOffset CreatedAt,DateTimeOffset? ProcessedAt);
public sealed record ExportReturnItem(Guid Id,Guid ReturnRequestId,Guid OrderItemId,string Sku,int Quantity,decimal RefundAmount);
public sealed record ExportWishlist(Guid Id,string Name,bool IsDefault,DateTimeOffset CreatedAt);
public sealed record ExportWishlistItem(Guid Id,Guid WishlistId,Guid ProductVariantId,string? Note,DateTimeOffset CreatedAt);
public sealed record ExportReview(Guid Id,Guid ProductId,int Rating,string Title,string Body,string Status,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt);
public sealed record AccountExportV1(int Version,DateTimeOffset GeneratedAt,ExportProfile Profile,IReadOnlyList<ExportAddress> Addresses,IReadOnlyList<ExportOrder> Orders,IReadOnlyList<ExportOrderItem> OrderItems,IReadOnlyList<ExportFulfillment> Fulfillments,IReadOnlyList<ExportFulfillmentItem> FulfillmentItems,IReadOnlyList<ExportReturn> Returns,IReadOnlyList<ExportReturnItem> ReturnItems,IReadOnlyList<ExportWishlist> Wishlists,IReadOnlyList<ExportWishlistItem> WishlistItems,IReadOnlyList<ExportReview> Reviews);
public sealed record AccountExportFile(byte[] Bytes,string FileName);

public sealed class AccountExportService(AgoraDbContext db,TimeProvider clock)
{
    public const int MaximumRecords=10_000; public const int MaximumBytes=5*1024*1024;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public async Task<AccountExportFile> CreateAsync(Guid owner,CancellationToken ct=default)
    {
        var generatedAt=clock.GetUtcNow();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var profile=await db.Customers.AsNoTracking().Where(x=>x.Id==owner).Select(x=>new ExportProfile(x.Id,x.Email,x.FullName,x.Role.ToString(),x.CreatedAt)).SingleOrDefaultAsync(ct)
            ??throw new NotFoundException("Account was not found.");
        var orderIds=db.Orders.Where(x=>x.CustomerId==owner).Select(x=>x.Id);
        var fulfillmentIds=db.Fulfillments.Where(x=>orderIds.Contains(x.OrderId)).Select(x=>x.Id);
        var returnIds=db.ReturnRequests.Where(x=>orderIds.Contains(x.OrderId)).Select(x=>x.Id);
        var wishlistIds=db.Wishlists.Where(x=>x.CustomerId==owner).Select(x=>x.Id);
        var remaining=MaximumRecords-1;
        async Task Consume<T>(IQueryable<T> query)
        {
            var observed=await query.Take(remaining+1).CountAsync(ct);
            if(observed>remaining)throw new AccountExportTooLargeException($"Account export exceeds {MaximumRecords} records.");
            remaining-=observed;
        }
        await Consume(db.CustomerAddresses.Where(x=>x.CustomerId==owner)); await Consume(orderIds);
        await Consume(db.OrderItems.Where(x=>orderIds.Contains(x.OrderId))); await Consume(fulfillmentIds);
        await Consume(db.FulfillmentItems.Where(x=>fulfillmentIds.Contains(x.FulfillmentId))); await Consume(returnIds);
        await Consume(db.ReturnRequestItems.Where(x=>returnIds.Contains(x.ReturnRequestId))); await Consume(wishlistIds);
        await Consume(db.WishlistItems.Where(x=>wishlistIds.Contains(x.WishlistId))); await Consume(db.Reviews.Where(x=>x.CustomerId==owner));
        var addresses=await db.CustomerAddresses.AsNoTracking().Where(x=>x.CustomerId==owner).OrderBy(x=>x.Id).Select(x=>new ExportAddress(x.Id,x.Label,x.IsDefault,x.Address.FullName,x.Address.Line1,x.Address.Line2,x.Address.City,x.Address.Region,x.Address.PostalCode,x.Address.Country,x.CreatedAt)).ToArrayAsync(ct);
        var orders=await db.Orders.AsNoTracking().Where(x=>x.CustomerId==owner).OrderBy(x=>x.Id).Select(x=>new ExportOrder(x.Id,x.Number,x.Status.ToString(),x.Currency,x.Subtotal,x.DiscountAmount,x.TaxAmount,x.ShippingAmount,x.Total,x.CreatedAt,x.PaidAt,x.FulfilledAt)).ToArrayAsync(ct);
        var items=await db.OrderItems.AsNoTracking().Where(x=>orderIds.Contains(x.OrderId)).OrderBy(x=>x.Id).Select(x=>new ExportOrderItem(x.Id,x.OrderId,x.Sku,x.ProductName,x.VariantName,x.UnitPrice,x.Quantity,x.LineTotal)).ToArrayAsync(ct);
        var fulfills=await db.Fulfillments.AsNoTracking().Where(x=>orderIds.Contains(x.OrderId)).OrderBy(x=>x.Id).Select(x=>new ExportFulfillment(x.Id,x.OrderId,x.Number,x.Carrier,x.TrackingNumber,x.TrackingStatus.ToString(),x.CreatedAt)).ToArrayAsync(ct);
        var fulfillItems=await db.FulfillmentItems.AsNoTracking().Where(x=>fulfillmentIds.Contains(x.FulfillmentId)).OrderBy(x=>x.Id).Select(x=>new ExportFulfillmentItem(x.Id,x.FulfillmentId,x.OrderItemId,x.Sku,x.Quantity)).ToArrayAsync(ct);
        var returns=await db.ReturnRequests.AsNoTracking().Where(x=>orderIds.Contains(x.OrderId)).OrderBy(x=>x.Id).Select(x=>new ExportReturn(x.Id,x.OrderId,x.Number,x.Status.ToString(),x.Reason.ToString(),x.Comment,x.RefundAmount,x.Currency,x.CreatedAt,x.ProcessedAt)).ToArrayAsync(ct);
        var returnItems=await db.ReturnRequestItems.AsNoTracking().Where(x=>returnIds.Contains(x.ReturnRequestId)).OrderBy(x=>x.Id).Select(x=>new ExportReturnItem(x.Id,x.ReturnRequestId,x.OrderItemId,x.Sku,x.Quantity,x.RefundAmount)).ToArrayAsync(ct);
        var wishlists=await db.Wishlists.AsNoTracking().Where(x=>x.CustomerId==owner).OrderBy(x=>x.Id).Select(x=>new ExportWishlist(x.Id,x.Name,x.IsDefault,x.CreatedAt)).ToArrayAsync(ct);
        var wishlistItems=await db.WishlistItems.AsNoTracking().Where(x=>wishlistIds.Contains(x.WishlistId)).OrderBy(x=>x.Id).Select(x=>new ExportWishlistItem(x.Id,x.WishlistId,x.ProductVariantId,x.Note,x.CreatedAt)).ToArrayAsync(ct);
        var reviews=await db.Reviews.AsNoTracking().Where(x=>x.CustomerId==owner).OrderBy(x=>x.Id).Select(x=>new ExportReview(x.Id,x.ProductId,x.Rating,x.Title,x.Body,x.Status.ToString(),x.CreatedAt,x.UpdatedAt)).ToArrayAsync(ct);
        var document=new AccountExportV1(1,generatedAt,profile,addresses,orders,items,fulfills,fulfillItems,returns,returnItems,wishlists,wishlistItems,reviews);
        var bytes=await SerializeBoundedAsync(document,ct);
        await tx.CommitAsync(ct);
        return new AccountExportFile(bytes,$"agora-account-export-{generatedAt:yyyyMMdd}.json");
    }

    public static async Task<byte[]> SerializeBoundedAsync(AccountExportV1 document,CancellationToken ct=default)
    {
        await using var buffer=new LimitedMemoryStream(MaximumBytes);
        try { await JsonSerializer.SerializeAsync(buffer,document,Json,ct); }
        catch(ExportByteLimitException) { throw new AccountExportTooLargeException($"Account export exceeds {MaximumBytes} bytes."); }
        return buffer.ToArray();
    }

    private sealed class ExportByteLimitException:Exception { }
    private sealed class LimitedMemoryStream(int maximum):MemoryStream
    {
        public override void Write(byte[] buffer,int offset,int count){Ensure(count);base.Write(buffer,offset,count);}
        public override void Write(ReadOnlySpan<byte> buffer){Ensure(buffer.Length);base.Write(buffer);}
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default){Ensure(buffer.Length);return base.WriteAsync(buffer,ct);}
        private void Ensure(int added){if(Length+added>maximum)throw new ExportByteLimitException();}
    }
}
