using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Route("api/shipping-methods")]
public class ShippingEligibilityController(AgoraDbContext db) : ControllerBase
{
    [HttpPost("eligibility")]
    public async Task<ActionResult<IReadOnlyList<EligibleShippingMethodResponse>>> Preview(ShippingEligibilityPreviewRequest request, CancellationToken ct)
    {
        var methods = await db.ShippingMethods.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Code).ThenBy(m => m.Id).ToListAsync(ct);
        var ids = methods.Select(m => m.Id).ToArray();
        var policies = await db.Set<ShippingEligibilityPolicy>().AsNoTracking().Where(p => ids.Contains(p.ShippingMethodId)).ToDictionaryAsync(p => p.ShippingMethodId, ct);
        return Ok(methods.Where(m => !policies.TryGetValue(m.Id, out var p) || ShippingEligibilityRules.Evaluate(p.Countries(), p.MaximumWeightGrams, request.Country, request.WeightGrams).Eligible)
            .Select(m => new EligibleShippingMethodResponse(m.Id, m.Code, m.Name, m.MinDays, m.MaxDays)).ToArray());
    }

    [Authorize(Roles = "Admin"), HttpGet("/api/admin/shipping-methods/{id:guid}/eligibility")]
    public async Task<ActionResult<ShippingEligibilityPolicyResponse>> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!await db.ShippingMethods.AnyAsync(m => m.Id == id, ct)) return NotFound();
        var policy = await db.Set<ShippingEligibilityPolicy>().AsNoTracking().SingleOrDefaultAsync(p => p.ShippingMethodId == id, ct);
        return Ok(new ShippingEligibilityPolicyResponse(id, policy?.Countries() ?? [], policy?.MaximumWeightGrams, policy?.Revision));
    }

    [Authorize(Roles = "Admin"), HttpPut("/api/admin/shipping-methods/{id:guid}/eligibility"), Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<ShippingEligibilityPolicyResponse>> Put(Guid id, PutShippingEligibilityRequest request, CancellationToken ct)
    {
        // Construct first so invalid replacements never mutate the tracked row.
        var candidate = new ShippingEligibilityPolicy(id, request.Countries, request.MaximumWeightGrams);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.ShippingMethods.AnyAsync(m => m.Id == id, ct)) return NotFound();
        var policy = await db.Set<ShippingEligibilityPolicy>().SingleOrDefaultAsync(p => p.ShippingMethodId == id, ct);
        if (policy?.Revision != request.ExpectedRevision) return Conflict(new ProblemDetails { Title = "Shipping eligibility policy changed. Reload its revision." });
        if (policy is null) { policy = candidate; db.Add(policy); }
        else policy.Replace(candidate.Countries(), candidate.MaximumWeightGrams);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); Response.Headers.CacheControl = "private, no-store";
        return Ok(new ShippingEligibilityPolicyResponse(id, policy.Countries(), policy.MaximumWeightGrams, policy.Revision));
    }
}
