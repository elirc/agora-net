using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/inventory/adjustments")]
public class InventoryAdjustmentsController(InventoryAdjustmentService service, AgoraDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<InventoryAdjustmentResponse>> Apply(InventoryAdjustmentRequest request, CancellationToken ct)
    {
        var actor = User.GetCustomerId();
        if (actor is null) return Unauthorized();
        var command = InventoryAdjustmentCommand.Create(request.OperationId, request.Reason,
            request.Lines.Select(l => new InventoryAdjustmentChange(l.VariantId, l.Delta, l.ExpectedVersion!.Value)).ToArray());
        var result = await service.ApplyAsync(actor.Value, command, ct);
        Response.Headers.CacheControl = "no-store";
        var response = InventoryAdjustmentResponse.From(result.Receipt);
        return result.Replayed ? Ok(response) : CreatedAtAction(nameof(Get), new { operationId = result.Receipt.Id }, response);
    }

    [HttpGet("{operationId:guid}")]
    public async Task<ActionResult<InventoryAdjustmentResponse>> Get(Guid operationId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var batch = await db.InventoryAdjustmentBatches.AsNoTracking().Include(b => b.Lines).SingleOrDefaultAsync(b => b.Id == operationId, ct);
        return batch is null ? NotFound() : Ok(InventoryAdjustmentResponse.From(batch));
    }
}
