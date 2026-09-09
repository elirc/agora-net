using Agora.Api.Contracts;
using Agora.Api.Filters;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), LocalSqliteWrite]
[Route("api/admin/variants/{id:guid}/quantity-pricing")]
public sealed class QuantityPricingController(AgoraDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<QuantityPricingResponse>> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var variant = await db.ProductVariants.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct);
        if (variant is null) return NotFound();
        var policy = await db.Set<VariantQuantityPricing>().AsNoTracking().Include(p => p.Tiers).SingleOrDefaultAsync(p => p.ProductVariantId == id, ct);
        return Ok(ResponseFor(variant, policy));
    }
    [HttpPut]
    public async Task<ActionResult<QuantityPricingResponse>> Put(Guid id, PutQuantityPricingRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var variant = await db.ProductVariants.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct);
        if (variant is null) return NotFound();
        var policy = await db.Set<VariantQuantityPricing>().Include(p => p.Tiers).SingleOrDefaultAsync(p => p.ProductVariantId == id, ct);
        if ((policy is null && request.ExpectedRevision is not null) || (policy is not null && request.ExpectedRevision != policy.Revision))
            return Conflict(new ProblemDetails { Title = "Reload the quantity-pricing policy before replacing it." });
        if (policy is null)
        {
            policy = new VariantQuantityPricing(id, request.Tiers, variant.Price.Amount);
            db.Set<VariantQuantityPricing>().Add(policy);
        }
        else policy.Replace(request.Tiers, variant.Price.Amount);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(ResponseFor(variant, policy));
    }
    private static QuantityPricingResponse ResponseFor(ProductVariant variant, VariantQuantityPricing? policy) => new(
        variant.Id, variant.Price.Currency, variant.Price.Amount, policy?.Revision,
        policy?.Tiers.OrderBy(t => t.MinimumQuantity).Select(t => new QuantityTierInput(t.MinimumQuantity, t.UnitAmount)).ToArray() ?? []);
}
