using Agora.Api.Contracts;
using Agora.Api.Queries;
using System.ComponentModel.DataAnnotations;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController(AgoraDbContext db, Agora.Infrastructure.Services.CategoryTreeService tree) : ControllerBase
{
    public const int MaxPageSize = 100;

    [HttpGet]
    public async Task<ActionResult<PagedResult<CategoryResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery, MaxLength(200)] string? search = null,
        [FromQuery] bool? rootOnly = null,
        [FromQuery] Guid? parentCategoryId = null,
        CancellationToken ct = default)
    {
        if (!QueryRules.ValidPage(page, pageSize, MaxPageSize))
        {
            return BadRequest(new ProblemDetails
            {
                Title = $"page must be >= 1 and pageSize between 1 and {MaxPageSize}.",
            });
        }

        if (rootOnly == true && parentCategoryId.HasValue)
            return BadRequest(new ProblemDetails { Title = "rootOnly and parentCategoryId cannot be combined." });

        var query = db.Categories.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = QueryRules.LiteralContains(search);
            query = query.Where(c => EF.Functions.Like(c.Name, pattern, "\\"));
        }
        if (rootOnly == true) query = query.Where(c => c.ParentCategoryId == null);
        if (parentCategoryId is { } parentId) query = query.Where(c => c.ParentCategoryId == parentId);
        var totalCount = await query.CountAsync(ct);
        var categories = await query.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return Ok(new PagedResult<CategoryResponse>(
            categories.Select(CategoryResponse.From).ToList(), page, pageSize, totalCount));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> GetById(Guid id, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return category is null ? NotFound() : Ok(CategoryResponse.From(category));
    }

    [HttpGet("by-slug/{slug}")]
    public async Task<ActionResult<CategoryResponse>> GetBySlug(string slug, CancellationToken ct)
    {
        var normalized = slug.Trim();
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == normalized, ct);
        return category is null ? NotFound() : Ok(CategoryResponse.From(category));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<CategoryResponse>> Create(CreateCategoryRequest request, CancellationToken ct)
    {
        var category = await tree.CreateAsync(request.Name, request.Slug, request.Description, request.ParentCategoryId, ct);
        return CreatedAtAction(nameof(GetById), new { id = category.Id }, CategoryResponse.From(category));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<CategoryResponse>> Update(Guid id, UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await tree.UpdateAsync(id, request.Name, request.Slug, request.Description, request.ParentCategoryId, ct);
        return Ok(CategoryResponse.From(category));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await tree.DeleteAsync(id, ct);
        return NoContent();
    }
}
