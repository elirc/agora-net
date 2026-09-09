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
[Route("api/admin/reports/replenishment")]
public class ReplenishmentController(AgoraDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ReplenishmentReportResponse>> Get([FromQuery] int windowDays = 30,
        [FromQuery] int coverDays = 14, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (windowDays is < 7 or > 90 || coverDays is < 1 or > 60 || !QueryRules.ValidPage(page, pageSize))
            return BadRequest(new ProblemDetails { Title = "Use windowDays 7–90, coverDays 1–60, and valid pagination." });
        var asOf = clock.GetUtcNow();
        var from = asOf.AddDays(-windowDays);
        Response.Headers.CacheControl = "no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var cohortLines = db.OrderItems.AsNoTracking().Where(i => i.Order!.PaidAt >= from && i.Order.PaidAt < asOf &&
            (i.Order.Status == OrderStatus.Paid || i.Order.Status == OrderStatus.PartiallyFulfilled || i.Order.Status == OrderStatus.Fulfilled));
        var sales = cohortLines.GroupBy(i => i.ProductVariantId).Select(g => new { VariantId = g.Key, Units = g.Sum(i => (long)i.Quantity) });
        var approved = from returned in db.ReturnRequestItems.AsNoTracking()
                       join line in cohortLines on returned.OrderItemId equals line.Id
                       where returned.ReturnRequest!.Status == ReturnStatus.Approved
                       group returned by line.ProductVariantId into grouped
                       select new { VariantId = grouped.Key, Units = (long?)grouped.Sum(i => (long)i.Quantity) };
        // Join aggregates, never raw sales and return child collections: each side has one row per variant.
        var demand = from sale in sales
                     join variant in db.ProductVariants on sale.VariantId equals variant.Id
                     where variant.Product!.IsActive
                     join returned in approved on variant.Id equals returned.VariantId into returns
                     from returned in returns.DefaultIfEmpty()
                     join stock in db.InventoryItems on variant.Id equals stock.ProductVariantId into stocks
                     from stock in stocks.DefaultIfEmpty()
                     select new { VariantId = variant.Id, variant.Sku, ProductName = variant.Product!.Name,
                         VariantName = variant.Name, Net = sale.Units - (returned.Units ?? 0),
                         Available = stock == null ? 0L : (long)stock.QuantityOnHand - stock.QuantityReserved };
        var maximumNet = (long.MaxValue - (windowDays - 1)) / coverDays;
        if (await demand.AnyAsync(d => d.Net < 0 || d.Net > maximumNet || d.Available < 0, ct))
            return Conflict(new ProblemDetails { Title = "Sales, returns, or inventory quantities are inconsistent or exceed supported arithmetic bounds." });
        // ceil(net * cover / window) - integer availability is exact; avoid early decimal rounding.
        var suggestions = demand.Select(d => new { d.VariantId, d.Sku, d.ProductName, d.VariantName, d.Net, d.Available,
            Suggested = (d.Net * coverDays + windowDays - 1) / windowDays - d.Available }).Where(d => d.Suggested > 0);
        var count = await suggestions.CountAsync(ct);
        var rows = await suggestions.OrderByDescending(d => d.Suggested).ThenBy(d => d.VariantId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new ReplenishmentReportResponse(asOf, from, asOf, windowDays, coverDays,
            new PagedResult<ReplenishmentRow>(rows.Select(r => new ReplenishmentRow(r.VariantId, r.Sku,
                r.ProductName, r.VariantName, r.Net, (decimal)r.Net / windowDays, r.Available, r.Suggested)).ToArray(), page, pageSize, count)));
    }
}
