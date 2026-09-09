using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize]
[Agora.Api.Filters.LocalSqliteWrite]
[Route("api/me/recent-products")]
public class RecentProductsController(AgoraDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPost("{productId:guid}")]
    public async Task<IActionResult> Record(Guid productId, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Products.AnyAsync(p => p.Id == productId && p.IsActive, ct) ||
            !await db.Customers.AnyAsync(c => c.Id == owner, ct)) return NotFound();
        // Capture after acquiring the writer transaction, not before waiting for another request.
        var now = clock.GetUtcNow();
        var existing = await db.RecentlyViewedProducts.SingleOrDefaultAsync(r => r.CustomerId == owner && r.ProductId == productId, ct);
        if (existing is null) db.RecentlyViewedProducts.Add(new RecentlyViewedProduct(owner.Value, productId, now));
        else existing.RecordView(now);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        { return Conflict(new ProblemDetails { Title = "The view was recorded concurrently. Retry the explicit view request." }); }
        var retained = await db.RecentlyViewedProducts.Where(r => r.CustomerId == owner)
            .OrderByDescending(r => r.LastViewedAt).ThenBy(r => r.ProductId).Take(50).Select(r => r.ProductId).ToArrayAsync(ct);
        await db.RecentlyViewedProducts.Where(r => r.CustomerId == owner && !retained.Contains(r.ProductId)).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecentProductResponse>>> List(CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var recent = await db.RecentlyViewedProducts.AsNoTracking().Where(r => r.CustomerId == owner && r.Product!.IsActive)
            .OrderByDescending(r => r.LastViewedAt).ThenBy(r => r.ProductId).Take(20)
            .Select(r => new { r.ProductId, r.LastViewedAt }).ToListAsync(ct);
        var ids = recent.Select(r => r.ProductId).ToList();
        var products = await ProductReadQueries.WithResponseData(db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)))
            .ToDictionaryAsync(p => p.Id, ct);
        var ratings = await ProductReadQueries.Ratings(db, ids, ct);
        await transaction.CommitAsync(ct);
        return Ok(recent.Select(r => new RecentProductResponse(r.LastViewedAt, ProductReadQueries.Response(products[r.ProductId], ratings))).ToArray());
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        await db.RecentlyViewedProducts.Where(r => r.CustomerId == owner).ExecuteDeleteAsync(ct);
        return NoContent();
    }
}
