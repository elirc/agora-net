using System.Globalization;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/reports")]
public class AdminReportsController(AgoraDbContext db) : ControllerBase
{
    /// <summary>Orders and revenue bucketed over time (by payment date).</summary>
    [HttpGet("sales")]
    public async Task<ActionResult<SalesReportResponse>> Sales(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string interval = "day",
        CancellationToken ct = default)
    {
        var normalizedInterval = interval.Trim().ToLowerInvariant();
        if (normalizedInterval is not ("day" or "week" or "month"))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "interval must be 'day', 'week', or 'month'.",
            });
        }

        var rangeTo = to ?? DateTimeOffset.UtcNow;
        var rangeFrom = from ?? rangeTo.AddDays(-30);
        if (rangeFrom > rangeTo)
        {
            return BadRequest(new ProblemDetails { Title = "'from' must not be after 'to'." });
        }

        // Bucketing happens in memory: SQLite stores DateTimeOffset as ticks,
        // which range-filters fine but does not support date functions.
        var orders = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.PaidAt != null && o.PaidAt >= rangeFrom && o.PaidAt <= rangeTo)
            .ToListAsync(ct);

        var buckets = orders
            .GroupBy(o => BucketKey(o.PaidAt!.Value, normalizedInterval))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new SalesBucketResponse(
                g.Key,
                g.Count(),
                g.Sum(o => o.Items.Sum(i => i.Quantity)),
                g.Sum(o => o.Total)))
            .ToList();

        return Ok(new SalesReportResponse(
            rangeFrom,
            rangeTo,
            normalizedInterval,
            orders.Count,
            orders.Sum(o => o.Total),
            buckets));
    }

    /// <summary>Best sellers by revenue within the (paid) date range.</summary>
    [HttpGet("top-products")]
    public async Task<ActionResult<List<TopProductResponse>>> TopProducts(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > 100)
        {
            return BadRequest(new ProblemDetails { Title = "limit must be between 1 and 100." });
        }

        var rangeTo = to ?? DateTimeOffset.UtcNow;
        var rangeFrom = from ?? rangeTo.AddDays(-30);

        if (rangeFrom > rangeTo)
            return BadRequest(new ProblemDetails { Title = "'from' must not be after 'to'." });

        var rows = await db.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.PaidAt != null
                        && i.Order.PaidAt >= rangeFrom
                        && i.Order.PaidAt <= rangeTo)
            .GroupBy(i => new { i.Sku, i.ProductName })
            .Select(g => new
            {
                g.Key.Sku,
                g.Key.ProductName,
                UnitsSold = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.LineTotal),
            })
            .OrderByDescending(r => r.Revenue)
            .ThenByDescending(r => r.UnitsSold)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(rows
            .Select(r => new TopProductResponse(r.Sku, r.ProductName, r.UnitsSold, r.Revenue))
            .ToList());
    }

    /// <summary>Variants at or below the availability threshold, scarcest first.</summary>
    [HttpGet("low-stock")]
    public async Task<ActionResult<PagedResult<LowStockResponse>>> LowStock(
        [FromQuery] int threshold = 5,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (threshold < 0)
        {
            return BadRequest(new ProblemDetails { Title = "threshold must be >= 0." });
        }

        if (page < 1 || pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "page must be >= 1 and pageSize between 1 and 100.",
            });
        }

        var query = db.InventoryItems
            .AsNoTracking()
            .Include(i => i.ProductVariant).ThenInclude(v => v!.Product)
            .Where(i => i.QuantityOnHand - i.QuantityReserved <= threshold)
            .OrderBy(i => i.QuantityOnHand - i.QuantityReserved);

        var totalCount = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return Ok(new PagedResult<LowStockResponse>(
            rows.Select(i => new LowStockResponse(
                    i.ProductVariant!.Sku,
                    i.ProductVariant.Product?.Name ?? string.Empty,
                    i.ProductVariant.Name,
                    i.QuantityOnHand,
                    i.QuantityReserved,
                    i.QuantityAvailable))
                .ToList(),
            page, pageSize, totalCount));
    }

    /// <summary>Redemption stats per discount code.</summary>
    [HttpGet("discount-usage")]
    public async Task<ActionResult<List<DiscountUsageResponse>>> DiscountUsage(CancellationToken ct)
    {
        var discounts = await db.DiscountCodes.AsNoTracking().OrderBy(d => d.Code).ToListAsync(ct);

        var orderStats = await db.Orders
            .AsNoTracking()
            .Where(o => o.DiscountCode != null && o.PaidAt != null)
            .GroupBy(o => o.DiscountCode!)
            .Select(g => new
            {
                Code = g.Key,
                OrderCount = g.Count(),
                TotalDiscounted = g.Sum(o => o.DiscountAmount),
                TotalRevenue = g.Sum(o => o.Total),
            })
            .ToDictionaryAsync(x => x.Code, ct);

        return Ok(discounts
            .Select(d =>
            {
                var stats = orderStats.GetValueOrDefault(d.Code);
                return new DiscountUsageResponse(
                    d.Code,
                    d.Type.ToString(),
                    d.TimesUsed,
                    stats?.OrderCount ?? 0,
                    stats?.TotalDiscounted ?? 0m,
                    stats?.TotalRevenue ?? 0m);
            })
            .ToList());
    }

    private static string BucketKey(DateTimeOffset paidAt, string interval)
    {
        var date = paidAt.UtcDateTime.Date;
        return interval switch
        {
            "month" => date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            "week" => WeekKey(date),
            _ => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }

    private static string WeekKey(DateTime date)
    {
        var week = ISOWeek.GetWeekOfYear(date);
        var year = ISOWeek.GetYear(date);
        return $"{year}-W{week:00}";
    }
}
