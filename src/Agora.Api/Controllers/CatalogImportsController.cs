using Agora.Api.Contracts;
using Agora.Api.Filters;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agora.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), LocalSqliteWrite]
[Route("api/admin/catalog-imports")]
public sealed class CatalogImportsController(CatalogImportService imports) : ControllerBase
{
    [HttpPost("preview"), RequestSizeLimit(1_048_576), CatalogImportBodyLimit]
    public async Task<ActionResult<CatalogImportView>> Preview(PreviewCatalogImportRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var rows = request.Products.Select(r => new CatalogImportRow(r.RowKey.Trim(), r.Product.ToDraft(forceInactive: true))).ToArray();
        var result = await imports.PreviewAsync(rows, Guid.Parse(User.FindFirst("sub")!.Value), ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CatalogImportView>> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var result = await imports.GetAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpPost("{id:guid}/commit")]
    public async Task<ActionResult<CatalogImportView>> Commit(Guid id, CommitCatalogImportRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var result = await imports.CommitAsync(id, request.Revision, request.Digest, ct);
        if (result.Status == 200) return Ok(result.Import);
        var problem = new ProblemDetails { Status = result.Status, Title = result.Error };
        if (result.RowErrors is not null) problem.Extensions["rows"] = result.RowErrors;
        return StatusCode(result.Status, problem);
    }
}
