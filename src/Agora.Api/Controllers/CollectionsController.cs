using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api")]
public class CollectionsController(AgoraDbContext db) : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpPost("admin/collections")]
    public async Task<ActionResult<CollectionAdminResponse>> Create(CreateCollectionRequest request, CancellationToken ct)
    {
        var collection = new ProductCollection(request.Title, request.Slug);
        if (await db.ProductCollections.AnyAsync(c => c.Slug == collection.Slug, ct))
            return Conflict(new ProblemDetails { Title = "Collection slug already exists." });
        db.ProductCollections.Add(collection);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        { return Conflict(new ProblemDetails { Title = "Collection slug already exists." }); }
        return CreatedAtAction(nameof(AdminGet), new { id = collection.Id }, CollectionAdminResponse.From(collection));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin/collections/{id:guid}")]
    public async Task<ActionResult<CollectionAdminResponse>> AdminGet(Guid id, CancellationToken ct)
    {
        var collection = await db.ProductCollections.AsNoTracking().Include(c => c.Items).SingleOrDefaultAsync(c => c.Id == id, ct);
        return collection is null ? NotFound() : Ok(CollectionAdminResponse.From(collection));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("admin/collections/{id:guid}")]
    public async Task<ActionResult<CollectionAdminResponse>> Replace(Guid id, ReplaceCollectionRequest request, CancellationToken ct)
    {
        var collection = await db.ProductCollections.Include(c => c.Items).SingleOrDefaultAsync(c => c.Id == id, ct);
        if (collection is null) return NotFound();
        if (collection.Version != request.ExpectedVersion) return Conflict(new ProblemDetails { Title = "Collection has changed. Reload before replacing." });
        if (request.ProductIds.Contains(Guid.Empty) || request.ProductIds.Distinct().Count() != request.ProductIds.Count)
            return UnprocessableEntity(new ProblemDetails { Title = "Product IDs must be distinct and nonempty." });
        if (await db.Products.CountAsync(p => request.ProductIds.Contains(p.Id), ct) != request.ProductIds.Count)
            return UnprocessableEntity(new ProblemDetails { Title = "Every member product must exist." });
        collection.Replace(request.Title, request.IsPublished, request.ProductIds);
        foreach (var item in collection.Items)
            if (db.Entry(item).State == EntityState.Detached) db.CollectionItems.Add(item);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        { return Conflict(new ProblemDetails { Title = "Collection changed concurrently. Reload before replacing." }); }
        return Ok(CollectionAdminResponse.From(collection));
    }

    [HttpGet("collections/{slug}")]
    public async Task<ActionResult<PublicCollectionResponse>> PublicGet(string slug,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!QueryRules.ValidPage(page, pageSize)) return BadRequest(new ProblemDetails { Title = "Invalid pagination." });
        var normalized = CatalogText.Slug(slug);
        var collection = await db.ProductCollections.AsNoTracking().SingleOrDefaultAsync(c => c.Slug == normalized && c.IsPublished, ct);
        if (collection is null) return NotFound();
        var members = db.CollectionItems.AsNoTracking().Where(i => i.CollectionId == collection.Id && i.Product!.IsActive);
        var count = await members.CountAsync(ct);
        var pageIds = await members.OrderBy(i => i.Position).ThenBy(i => i.ProductId)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(i => i.ProductId).ToListAsync(ct);
        var products = await ProductReadQueries.WithResponseData(db.Products.AsNoTracking()
            .Where(p => pageIds.Contains(p.Id) && p.IsActive)).ToListAsync(ct);
        var byId = products.ToDictionary(p => p.Id);
        var ordered = pageIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        var ratings = await ProductReadQueries.Ratings(db, products.Select(p => p.Id).ToList(), ct);
        return Ok(new PublicCollectionResponse(collection.Id, collection.Title, collection.Slug,
            new PagedResult<ProductResponse>(ordered.Select(p => ProductReadQueries.Response(p, ratings)).ToArray(), page, pageSize, count)));
    }
}
