using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Api.Filters;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/inventory-counts")]
public sealed class InventoryCountsController(InventoryCountService service) : ControllerBase
{
    [HttpPost]
    [LocalSqliteWrite]
    public async Task<ActionResult<InventoryCountResponse>> Create(
        CreateInventoryCountRequest request,
        CancellationToken ct)
    {
        var actor = User.GetCustomerId();
        if (actor is null) return Unauthorized();

        var session = await service.CreateAsync(actor.Value, request.VariantIds, ct);
        return CreatedAtAction(nameof(Get), new { id = session.Id }, InventoryCountResponse.From(session));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InventoryCountResponse>> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var session = await service.ReadAsync(id, ct);
        return session is null ? NotFound() : Ok(InventoryCountResponse.From(session));
    }

    [HttpPut("{id:guid}/lines/{lineId:guid}")]
    [LocalSqliteWrite]
    public async Task<ActionResult<InventoryCountResponse>> Record(
        Guid id,
        Guid lineId,
        RecordInventoryCountRequest request,
        CancellationToken ct)
    {
        var session = await service.RecordAsync(
            id, lineId, request.CountedQuantity, request.ExpectedRevision!.Value, ct);
        return Ok(InventoryCountResponse.From(session));
    }

    [HttpPost("{id:guid}/apply")]
    [LocalSqliteWrite]
    public async Task<ActionResult<InventoryCountResponse>> Apply(
        Guid id,
        RevisionRequest request,
        CancellationToken ct)
    {
        var actor = User.GetCustomerId();
        if (actor is null) return Unauthorized();

        var session = await service.ApplyAsync(id, actor.Value, request.ExpectedRevision!.Value, ct);
        return Ok(InventoryCountResponse.From(session));
    }

    [HttpPost("{id:guid}/cancel")]
    [LocalSqliteWrite]
    public async Task<ActionResult<InventoryCountResponse>> Cancel(
        Guid id,
        RevisionRequest request,
        CancellationToken ct)
    {
        var actor = User.GetCustomerId();
        if (actor is null) return Unauthorized();

        var session = await service.CancelAsync(id, actor.Value, request.ExpectedRevision!.Value, ct);
        return Ok(InventoryCountResponse.From(session));
    }
}
