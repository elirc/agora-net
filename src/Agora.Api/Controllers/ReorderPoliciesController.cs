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
[Authorize(Roles = "Admin")]
[Route("api/admin/inventory")]
public class ReorderPoliciesController(AgoraDbContext db, TimeProvider clock) : ControllerBase
{
    private static ReorderPolicyResponse ResponseFor(Guid id, InventoryReorderPolicy? policy) => new(id,
        policy is not null, policy?.Threshold ?? InventoryReorderPolicy.DefaultThreshold,
        policy?.TargetLevel ?? InventoryReorderPolicy.DefaultTargetLevel, policy?.Version, policy?.UpdatedAt);

    [HttpGet("{variantId:guid}/reorder-policy")]
    public async Task<ActionResult<ReorderPolicyResponse>> Get(Guid variantId, CancellationToken ct)
    {
        if (!await db.ProductVariants.AnyAsync(v => v.Id == variantId, ct)) return NotFound();
        return Ok(ResponseFor(variantId, await db.InventoryReorderPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.ProductVariantId == variantId, ct)));
    }

    [HttpPut("{variantId:guid}/reorder-policy")]
    public async Task<ActionResult<ReorderPolicyResponse>> Put(Guid variantId, ReplaceReorderPolicyRequest request, CancellationToken ct)
    {
        if (!await db.ProductVariants.AnyAsync(v => v.Id == variantId, ct)) return NotFound();
        var policy = await db.InventoryReorderPolicies.SingleOrDefaultAsync(p => p.ProductVariantId == variantId, ct);
        if (policy?.Version != request.ExpectedVersion || (policy is not null && request.ExpectedVersion is null))
            return Conflict(new ProblemDetails { Title = "Reorder policy changed. Reload its current revision." });
        if (policy is null)
        {
            policy = new InventoryReorderPolicy(variantId, request.Threshold!.Value, request.TargetLevel!.Value, clock.GetUtcNow());
            db.InventoryReorderPolicies.Add(policy);
        }
        else policy.Replace(request.Threshold!.Value, request.TargetLevel!.Value, clock.GetUtcNow());
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        { return Conflict(new ProblemDetails { Title = "Another request created the reorder policy. Reload before updating." }); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 787 })
        { return NotFound(); }
        return Ok(ResponseFor(variantId, policy));
    }

    [HttpGet("reorder-report")]
    public async Task<ActionResult<PagedResult<ReorderReportRow>>> Report([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!QueryRules.ValidPage(page, pageSize)) return BadRequest(new ProblemDetails { Title = "Invalid pagination." });
        Response.Headers.CacheControl = "no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Inventory is the root: variants lacking a stock record cannot supply a stock observation.
        var rows = from stock in db.InventoryItems.AsNoTracking()
                   join variant in db.ProductVariants on stock.ProductVariantId equals variant.Id
                   join policy in db.InventoryReorderPolicies on variant.Id equals policy.ProductVariantId into policies
                   from policy in policies.DefaultIfEmpty()
                   let available = (long)stock.QuantityOnHand - stock.QuantityReserved
                   let threshold = policy == null ? InventoryReorderPolicy.DefaultThreshold : policy.Threshold
                   let target = policy == null ? InventoryReorderPolicy.DefaultTargetLevel : policy.TargetLevel
                   where available <= threshold
                   select new { VariantId = variant.Id, variant.Sku, ProductName = variant.Product!.Name,
                       VariantName = variant.Name, OnHand = stock.QuantityOnHand, Reserved = stock.QuantityReserved,
                       Available = available, HasOverride = policy != null, Threshold = threshold, TargetLevel = target,
                       SuggestedQuantity = target > available ? target - available : 0 };
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(r => r.SuggestedQuantity).ThenBy(r => r.VariantId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new PagedResult<ReorderReportRow>(items.Select(r => new ReorderReportRow(r.VariantId, r.Sku,
            r.ProductName, r.VariantName, r.OnHand, r.Reserved, r.Available, r.HasOverride, r.Threshold,
            r.TargetLevel, r.SuggestedQuantity)).ToArray(), page, pageSize, total));
    }
}
