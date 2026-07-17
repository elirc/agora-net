using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController(AgoraDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CategoryResponse>>> List(CancellationToken ct)
    {
        var categories = await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return Ok(categories.Select(CategoryResponse.From).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> GetById(Guid id, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return category is null ? NotFound() : Ok(CategoryResponse.From(category));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<CategoryResponse>> Create(CreateCategoryRequest request, CancellationToken ct)
    {
        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? SlugGenerator.FromName(request.Name)
            : request.Slug.Trim();

        if (await db.Categories.AnyAsync(c => c.Slug == slug, ct))
        {
            return Conflict(new ProblemDetails { Title = $"A category with slug '{slug}' already exists." });
        }

        if (request.ParentCategoryId is { } parentId
            && !await db.Categories.AnyAsync(c => c.Id == parentId, ct))
        {
            return UnprocessableEntity(new ProblemDetails { Title = "Parent category does not exist." });
        }

        var category = new Category
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId,
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = category.Id }, CategoryResponse.From(category));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> Update(Guid id, UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return NotFound();
        }

        if (await db.Categories.AnyAsync(c => c.Slug == request.Slug && c.Id != id, ct))
        {
            return Conflict(new ProblemDetails { Title = $"A category with slug '{request.Slug}' already exists." });
        }

        if (request.ParentCategoryId == id)
        {
            return UnprocessableEntity(new ProblemDetails { Title = "A category cannot be its own parent." });
        }

        category.Name = request.Name.Trim();
        category.Slug = request.Slug.Trim();
        category.Description = request.Description;
        category.ParentCategoryId = request.ParentCategoryId;
        await db.SaveChangesAsync(ct);

        return Ok(CategoryResponse.From(category));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return NotFound();
        }

        var inUse = await db.Products.AnyAsync(p => p.CategoryId == id, ct)
                    || await db.Categories.AnyAsync(c => c.ParentCategoryId == id, ct);
        if (inUse)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Category has products or child categories and cannot be deleted.",
            });
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
