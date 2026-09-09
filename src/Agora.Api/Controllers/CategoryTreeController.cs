using Agora.Api.Contracts;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agora.Api.Controllers;

[ApiController, Agora.Api.Filters.LocalSqliteWrite]
[Route("api")]
public class CategoryTreeController(CategoryTreeService tree) : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpGet("admin/category-tree")]
    public async Task<ActionResult<CategoryTreeSnapshot>> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store"; return Ok(await tree.ReadAsync(ct));
    }
    [Authorize(Roles = "Admin")]
    [HttpGet("admin/category-tree/integrity")]
    public async Task<IActionResult> Integrity(CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var snapshot = await tree.ReadAsync(ct); return Ok(new { snapshot.Version, snapshot.IsValid, snapshot.Issues, NodeCount = snapshot.Nodes.Count });
    }
    [Authorize(Roles = "Admin")]
    [HttpPost("admin/categories/{id:guid}/move")]
    public async Task<ActionResult<CategoryMoveResponse>> Move(Guid id, MoveCategoryRequest request, CancellationToken ct)
    {
        var result = await tree.MoveAsync(id, request.NewParentCategoryId, request.ExpectedTreeVersion!.Value, ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(new CategoryMoveResponse(CategoryResponse.From(result.Category), result.Version));
    }
    [HttpGet("categories/{id:guid}/breadcrumbs")]
    public async Task<ActionResult<IReadOnlyList<CategoryTreeNode>>> Breadcrumbs(Guid id, CancellationToken ct) => Ok(await tree.BreadcrumbsAsync(id, ct));
}
