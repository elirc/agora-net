using Agora.Api.Contracts;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/products")]
public class ProductCloningController(AgoraDbContext db, CategoryOptionSchemaService optionSchemas,
    CatalogMutationService catalogFeed, TimeProvider clock) : ControllerBase
{
    [HttpPost("{sourceId:guid}/clone")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<ClonedProductResponse>> Clone(Guid sourceId, CloneProductRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // One SQL read gives a consistent source graph. The 51st variant is only an over-limit sentinel.
        var source = await db.Products.AsNoTracking()
            .Include(p => p.Variants.OrderBy(v => v.Id).Take(51)).Include(p => p.Images)
            .AsSingleQuery().SingleOrDefaultAsync(p => p.Id == sourceId, ct);
        if (source is null) return NotFound();
        if (source.Variants.Count > 50) return UnprocessableEntity(new ProblemDetails { Title = "Only products with at most 50 variants can be cloned." });
        var mappings = request.VariantSkus;
        if (mappings.Select(m => m.SourceVariantId).Distinct().Count() != mappings.Count
            || !source.Variants.Select(v => v.Id).ToHashSet().SetEquals(mappings.Select(m => m.SourceVariantId)))
            return UnprocessableEntity(new ProblemDetails { Title = "Supply exactly one SKU mapping for every source variant." });
        var skus = ProductInputRules.NormalizeSkus(mappings.Select(m => m.Sku));
        if (ProductInputRules.HasDuplicateSkus(skus)) return UnprocessableEntity(new ProblemDetails { Title = "Duplicate SKUs in request." });
        var slug = request.Slug.Trim();
        if (await db.Products.AnyAsync(p => p.Slug == slug, ct) || await db.ProductVariants.AnyAsync(v => skus.Contains(v.Sku), ct))
            return Conflict(new ProblemDetails { Title = "The new slug or a new SKU already exists." });
        var skuMap = mappings.Select((mapping, index) => (mapping.SourceVariantId, Sku: skus[index]))
            .ToDictionary(m => m.SourceVariantId, m => m.Sku);
        var clone = ProductDraftCloner.Clone(source, request.Name, slug, skuMap);
        await optionSchemas.ValidateAuthoringAsync(clone.CategoryId,
            clone.Variants.Select(v => new VariantOptionCandidate(v.Id, v.Sku, v.Options)).ToArray(), ct);
        db.Products.Add(clone);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        { return Conflict(new ProblemDetails { Title = "The new slug or a new SKU was taken concurrently." }); }
        await catalogFeed.StageUpsertAsync(clone, clock.GetUtcNow(), ct);
        await transaction.CommitAsync(ct);
        return Created($"/api/products/{clone.Id}", new ClonedProductResponse(clone.Id, clone.Slug, clone.IsActive));
    }
}
