using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Agora.Api.Filters.LocalSqliteWrite]
[Route("api/admin/categories/{id:guid}/option-schema")]
public class CategoryOptionSchemasController(AgoraDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CategoryOptionSchemaResponse>> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!await db.Categories.AnyAsync(c => c.Id == id, ct)) return NotFound();
        var schema = await db.CategoryOptionSchemas.AsNoTracking().SingleOrDefaultAsync(s => s.CategoryId == id, ct);
        return Ok(new CategoryOptionSchemaResponse(id, schema?.Mode.ToString() ?? "Off", schema?.SchemaVersion ?? 1, schema?.Revision, schema?.ReadRules() ?? []));
    }

    [HttpPut]
    public async Task<ActionResult<CategoryOptionSchemaResponse>> Put(Guid id, PutCategoryOptionSchemaRequest request, CancellationToken ct)
    {
        if (!QueryRules.TryNamedEnum<CategoryOptionSchemaMode>(request.Mode, out var mode))
            return BadRequest(new ProblemDetails { Title = "Mode must be Off, Observe, or Enforce." });
        var rules = CategoryOptionSchemaRules.Normalize(request.Rules.Select(r => r is null ? null! : new CategoryOptionRule(r.Key, r.Required, r.AllowedValues)).ToArray());
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Categories.AnyAsync(c => c.Id == id, ct)) return NotFound();
        var schema = await db.CategoryOptionSchemas.SingleOrDefaultAsync(s => s.CategoryId == id, ct);
        if (schema?.Revision != request.ExpectedRevision) return Conflict(new ProblemDetails { Title = "Option schema changed. Reload its revision." });
        if (schema is null) { schema = new CategoryOptionSchema(id, mode, rules); db.CategoryOptionSchemas.Add(schema); }
        else schema.Replace(mode, rules);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); Response.Headers.CacheControl = "private, no-store";
        return Ok(new CategoryOptionSchemaResponse(id, mode.ToString(), schema.SchemaVersion, schema.Revision, schema.ReadRules()));
    }

    [HttpGet("violations")]
    public async Task<ActionResult<PagedResult<CategoryOptionViolationResponse>>> Violations(Guid id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!QueryRules.ValidPage(page, pageSize)) return BadRequest(new ProblemDetails { Title = "Invalid pagination." });
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Categories.AnyAsync(c => c.Id == id, ct)) return NotFound();
        var schema = await db.CategoryOptionSchemas.AsNoTracking().SingleOrDefaultAsync(s => s.CategoryId == id, ct);
        if (schema is null) return Ok(new PagedResult<CategoryOptionViolationResponse>([], page, pageSize, 0));
        var rules = schema.ReadRules();
        var candidates = await db.ProductVariants.AsNoTracking().Where(v => v.Product!.CategoryId == id).OrderBy(v => v.Id).Take(10001)
            .Select(v => new { v.Id, v.Sku, v.Options }).ToListAsync(ct);
        if (candidates.Count > 10000) return UnprocessableEntity(new ProblemDetails { Title = "The violations report supports at most 10,000 category variants." });
        // Filter violations before counting/paging; candidate paging would hide matching rows.
        var violations = candidates.Select(v => new CategoryOptionViolationResponse(v.Id, v.Sku, CategoryOptionSchemaRules.Validate(rules, v.Options)))
            .Where(v => v.Violations.Count > 0).OrderBy(v => v.Sku, StringComparer.Ordinal).ThenBy(v => v.VariantId).ToArray();
        await transaction.CommitAsync(ct);
        return Ok(new PagedResult<CategoryOptionViolationResponse>(violations.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), page, pageSize, violations.Length));
    }
}
