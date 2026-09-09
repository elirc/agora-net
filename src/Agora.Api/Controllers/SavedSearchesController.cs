using System.Text.Json;
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
[Agora.Api.Filters.LocalSqliteWrite]
[Route("api/me/saved-searches")]
public class SavedSearchesController(AgoraDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SavedSearchResponse>> Create(CreateSavedSearchRequest request, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        var definition = JsonSerializer.Serialize(request.Definition, SavedSearchPayload.Options);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Customers.AnyAsync(c => c.Id == owner, ct)) return NotFound();
        if (await db.SavedCatalogSearches.CountAsync(s => s.CustomerId == owner, ct) >= 50)
            return Conflict(new ProblemDetails { Title = "An account can store at most 50 saved searches." });
        var saved = new SavedCatalogSearch(owner.Value, request.Name, definition, clock.GetUtcNow());
        db.SavedCatalogSearches.Add(saved); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return CreatedAtAction(nameof(Get), new { id = saved.Id }, SavedSearchPayload.Response(saved));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavedSearchResponse>>> List(CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        var rows = await db.SavedCatalogSearches.AsNoTracking().Where(s => s.CustomerId == owner)
            .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).ToListAsync(ct);
        return Ok(rows.Select(SavedSearchPayload.Response).ToArray());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SavedSearchResponse>> Get(Guid id, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        var saved = await db.SavedCatalogSearches.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.CustomerId == owner, ct);
        return saved is null ? NotFound() : Ok(SavedSearchPayload.Response(saved));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        return await db.SavedCatalogSearches.Where(s => s.Id == id && s.CustomerId == owner).ExecuteDeleteAsync(ct) == 0 ? NotFound() : NoContent();
    }

    [HttpGet("{id:guid}/results")]
    public async Task<ActionResult<PagedResult<ProductResponse>>> Results(Guid id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!QueryRules.ValidPage(page, pageSize)) return BadRequest(new ProblemDetails { Title = "Invalid pagination." });
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        var saved = await db.SavedCatalogSearches.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.CustomerId == owner, ct);
        if (saved is null) return NotFound();
        var (definition, error) = SavedSearchPayload.Interpret(saved);
        if (error is not null) return Conflict(new ProblemDetails { Title = "Saved definition cannot be run", Detail = error });
        Response.Headers.CacheControl = "private, no-store";
        return Ok(await ProductReadQueries.Page(db, definition!.ToRequest(page, pageSize), ct));
    }
}
