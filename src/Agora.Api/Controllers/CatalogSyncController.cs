using Agora.Domain.Entities;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize(Roles="Admin")]
[Route("api/admin/catalog-sync")]
public sealed class CatalogSyncController(CatalogFeedService feed):ControllerBase
{
    [HttpGet("bootstrap")]
    public async Task<ActionResult<CatalogBootstrapResult>> Bootstrap(CancellationToken ct)
    {Response.Headers.CacheControl="private, no-store";return Ok(await feed.BootstrapAsync(ct));}

    [HttpGet("changes")]
    public async Task<ActionResult<CatalogChangesResult>> Changes([FromQuery]long after=0,[FromQuery]int limit=100,CancellationToken ct=default)
    {Response.Headers.CacheControl="private, no-store";try{return Ok(await feed.ChangesAsync(after,limit,ct));}catch(CatalogCursorException error)when(error.Expired){return StatusCode(StatusCodes.Status410Gone,new ProblemDetails{Status=410,Title="Catalog cursor expired",Detail=error.Message});}}

    [HttpPost("purge")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<CatalogPurgeResult>> Purge(CancellationToken ct)
    {Response.Headers.CacheControl="private, no-store";return Ok(await feed.PurgeAsync(ct));}
}
