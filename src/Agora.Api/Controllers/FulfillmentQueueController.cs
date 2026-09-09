using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/fulfillment-queue")]
public class FulfillmentQueueController(AgoraDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<FulfillmentQueueOrderResponse>>> Get(
        [FromQuery] DateTimeOffset? paidFrom = null, [FromQuery] DateTimeOffset? paidTo = null,
        [FromQuery] bool? held = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!QueryRules.ValidPage(page, pageSize) || paidFrom.HasValue != paidTo.HasValue ||
            (paidFrom.HasValue && (paidFrom >= paidTo || paidTo - paidFrom > TimeSpan.FromDays(90))))
            return BadRequest(new ProblemDetails { Title = "Use valid pagination and an increasing paired date interval of at most 90 days." });
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var candidates = db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Paid || o.Status == OrderStatus.PartiallyFulfilled);
        if (paidFrom.HasValue) candidates = candidates.Where(o => o.PaidAt >= paidFrom && o.PaidAt < paidTo);
        // Correlated sums keep each order line independent even when it has several shipments.
        var quantities = db.OrderItems.AsNoTracking().Select(i => new
        {
            i.Id, i.OrderId, i.Sku, i.ProductName, i.VariantName, i.Quantity,
            Fulfilled = db.FulfillmentItems.Where(f => f.OrderItemId == i.Id).Sum(f => (long?)f.Quantity) ?? 0,
        });
        if (await quantities.AnyAsync(i => candidates.Any(o => o.Id == i.OrderId) &&
                (i.Fulfilled < 0 || i.Fulfilled > i.Quantity), ct))
            return Conflict(new ProblemDetails { Title = "Order fulfillment quantities are inconsistent." });
        var eligible = candidates.Where(o => quantities.Any(i => i.OrderId == o.Id && i.Quantity > i.Fulfilled));
        if (held.HasValue)
            eligible = eligible.Where(order =>
                db.Set<OrderHold>().Any(hold => hold.OrderId == order.Id && hold.IsActive) == held.Value);
        var total = await eligible.CountAsync(ct);
        var orders = await eligible.OrderBy(o => o.PaidAt).ThenBy(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new { o.Id, o.Number, o.PaidAt, o.ShippingMethodCode, o.ShippingMethodName,
                IsHeld = db.Set<OrderHold>().Any(hold => hold.OrderId == o.Id && hold.IsActive) }).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToArray();
        var lines = await quantities.Where(i => ids.Contains(i.OrderId) && i.Quantity > i.Fulfilled)
            .OrderBy(i => i.Sku).ThenBy(i => i.Id).ToListAsync(ct);
        var byOrder = lines.ToLookup(i => i.OrderId);
        await transaction.CommitAsync(ct);
        return Ok(new PagedResult<FulfillmentQueueOrderResponse>(orders.Select(o => new FulfillmentQueueOrderResponse(
            o.Number, o.PaidAt, o.ShippingMethodCode, o.ShippingMethodName, o.IsHeld,
            byOrder[o.Id].Select(i => new FulfillmentQueueLineResponse(i.Id, i.Sku, i.ProductName, i.VariantName,
                i.Quantity, i.Fulfilled, i.Quantity - i.Fulfilled)).ToArray())).ToArray(), page, pageSize, total));
    }
}
