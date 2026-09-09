using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api")]
public class TagsController(AgoraDbContext db) : ControllerBase
{
    [HttpGet("tags")]
    public async Task<ActionResult<List<TagResponse>>> List(CancellationToken ct) =>
        Ok(await db.Tags.AsNoTracking().OrderBy(t => t.Slug).ThenBy(t => t.Id)
            .Select(t => new TagResponse(t.Id, t.Name, t.Slug)).ToListAsync(ct));

    [Authorize(Roles = "Admin")]
    [HttpPost("admin/tags")]
    public async Task<ActionResult<TagResponse>> Create(CreateTagRequest request, CancellationToken ct)
    {
        var tag = new Tag(request.Name, request.Slug);
        if (await db.Tags.AnyAsync(t => t.Slug == tag.Slug, ct)) return Conflict(new ProblemDetails { Title = "Tag slug already exists." });
        db.Tags.Add(tag);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        { return Conflict(new ProblemDetails { Title = "Tag slug already exists." }); }
        return Created("/api/tags", TagResponse.From(tag));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("admin/products/{id:guid}/tags")]
    public async Task<ActionResult<ProductTagsResponse>> Replace(Guid id, ReplaceProductTagsRequest request, CancellationToken ct)
    {
        var product = await db.Products.Include(p => p.Tags).ThenInclude(t => t.Tag).SingleOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (product.TagVersion != request.ExpectedVersion) return Conflict(new ProblemDetails { Title = "Product tags have changed. Reload before replacing." });
        var tags = await db.Tags.Where(t => request.TagIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        if (tags.Count != request.TagIds.Count) return UnprocessableEntity(new ProblemDetails { Title = "Every tag must exist." });
        product.ReplaceTags(request.TagIds);
        foreach (var link in product.Tags)
        {
            link.Tag = tags[link.TagId];
            if (db.Entry(link).State == EntityState.Detached) db.ProductTags.Add(link);
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        { return Conflict(new ProblemDetails { Title = "Product tags changed concurrently. Reload before replacing." }); }
        return Ok(new ProductTagsResponse(product.Tags.OrderBy(t => t.Tag!.Slug, StringComparer.Ordinal)
            .Select(t => TagResponse.From(t.Tag!)).ToArray(), product.TagVersion));
    }
}
