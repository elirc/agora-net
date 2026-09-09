using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/me/orders/{number}/timeline")]
public class OrderTimelinesController(AgoraDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderTimelineEntry>>> Get(string number,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!QueryRules.ValidPage(page, pageSize) || (long)(page - 1) * pageSize > 10_000)
            return BadRequest(new ProblemDetails { Title = "Use pageSize 1–100 and an offset no greater than 10,000." });
        var owner = User.GetCustomerId();
        if (owner is null) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.AsNoTracking().Where(o => o.Number == number && o.CustomerId == owner)
            .Select(o => new { o.Id, o.Number, o.CreatedAt, o.PaidAt, o.FulfilledAt, o.CancelledAt, o.RefundedAt }).SingleOrDefaultAsync(ct);
        if (order is null) return NotFound();
        var fixedEvents = new List<OrderTimelineEntry>();
        void Add(string key, string type, DateTimeOffset? at, string label)
        {
            if (at is { } time) fixedEvents.Add(new OrderTimelineEntry($"{key}:{order.Id:D}", type, time, order.Id, order.Number, label));
        }
        Add("order-created", "OrderCreated", order.CreatedAt, "Order created");
        Add("order-paid", "OrderPaid", order.PaidAt, "Payment recorded");
        Add("order-fulfilled", "OrderFulfilled", order.FulfilledAt, "Order fully fulfilled");
        Add("order-cancelled", "OrderCancelled", order.CancelledAt, "Order cancelled");
        Add("order-refunded", "OrderRefunded", order.RefundedAt, "Order refunded");
        var shipments = db.Fulfillments.AsNoTracking().Where(f => f.OrderId == order.Id);
        var returns = db.ReturnRequests.AsNoTracking().Where(r => r.OrderId == order.Id);
        var processed = returns.Where(r => r.ProcessedAt != null &&
            (r.Status == ReturnStatus.Approved || r.Status == ReturnStatus.Rejected || r.Status == ReturnStatus.Cancelled));
        var total = fixedEvents.Count + await shipments.LongCountAsync(ct) + await returns.LongCountAsync(ct) + await processed.LongCountAsync(ct);
        if (total > int.MaxValue) return UnprocessableEntity(new ProblemDetails { Title = "The timeline exceeds the supported entry count." });
        var offset = (page - 1) * pageSize;
        var prefixLength = offset + pageSize;
        // The global first K entries can only contain entries from each source's first K.
        var shipmentRows = await shipments.OrderBy(f => f.CreatedAt).ThenBy(f => f.Id).Take(prefixLength)
            .Select(f => new { f.Id, f.Number, f.CreatedAt }).ToListAsync(ct);
        var returnRows = await returns.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Take(prefixLength)
            .Select(r => new { r.Id, r.Number, r.CreatedAt }).ToListAsync(ct);
        var processedRows = await processed.OrderBy(r => r.ProcessedAt).ThenBy(r => r.Id).Take(prefixLength)
            .Select(r => new { r.Id, r.Number, r.ProcessedAt, r.Status }).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        fixedEvents.AddRange(shipmentRows.Select(f => new OrderTimelineEntry($"fulfillment-created:{f.Id:D}", "FulfillmentCreated", f.CreatedAt, f.Id, f.Number, "Shipment created")));
        fixedEvents.AddRange(returnRows.Select(r => new OrderTimelineEntry($"return-created:{r.Id:D}", "ReturnCreated", r.CreatedAt, r.Id, r.Number, "Return requested")));
        fixedEvents.AddRange(processedRows.Select(r => new OrderTimelineEntry($"return-processed:{r.Id:D}", "Return" + r.Status, r.ProcessedAt!.Value,
            r.Id, r.Number, "Return " + r.Status.ToString().ToLowerInvariant())));
        var items = fixedEvents.OrderBy(e => e.RecordedAt).ThenBy(e => e.Key, StringComparer.Ordinal).Skip(offset).Take(pageSize).ToArray();
        return Ok(new PagedResult<OrderTimelineEntry>(items, page, pageSize, (int)total));
    }
}
