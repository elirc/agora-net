using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Api.Filters;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin")]
public sealed class PurchaseOrdersController(PurchaseOrderService service, AgoraDbContext db) : ControllerBase
{
    [HttpPost("suppliers")]
    [LocalSqliteWrite]
    public async Task<ActionResult<SupplierResponse>> CreateSupplier(
        CreateSupplierRequest request, CancellationToken ct)
    {
        var supplier = await service.CreateSupplierAsync(request.Name, request.Reference, ct);
        return CreatedAtAction(nameof(ListSuppliers), SupplierResponse.From(supplier));
    }

    [HttpGet("suppliers")]
    public async Task<ActionResult<IReadOnlyList<SupplierResponse>>> ListSuppliers(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var suppliers = await db.Set<Supplier>().AsNoTracking()
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new SupplierResponse(x.Id, x.Name, x.Reference, x.IsActive, x.CreatedAt))
            .ToArrayAsync(ct);
        return Ok(suppliers);
    }

    [HttpPost("suppliers/{id:guid}/deactivate")]
    [LocalSqliteWrite]
    public async Task<ActionResult<SupplierResponse>> Deactivate(Guid id, CancellationToken ct)
    {
        var supplier = await service.DeactivateSupplierAsync(id, ct);
        return Ok(SupplierResponse.From(supplier));
    }

    [HttpPost("purchase-orders")]
    [LocalSqliteWrite]
    public async Task<ActionResult<PurchaseOrderResponse>> Create(
        CreatePurchaseOrderRequest request, CancellationToken ct)
    {
        var lines = request.Lines.Select(line => (line.VariantId, line.Quantity)).ToArray();
        var order = await service.CreateAsync(request.SupplierId, lines, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, PurchaseOrderResponse.From(order));
    }

    [HttpGet("purchase-orders/{id:guid}")]
    public async Task<ActionResult<PurchaseOrderResponse>> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var order = await service.ReadAsync(id, ct);
        return order is null ? NotFound() : Ok(PurchaseOrderResponse.From(order));
    }

    [HttpPost("purchase-orders/{id:guid}/submit")]
    [LocalSqliteWrite]
    public async Task<ActionResult<PurchaseOrderResponse>> Submit(
        Guid id, RevisionRequest request, CancellationToken ct)
    {
        var order = await service.SubmitAsync(id, request.ExpectedRevision!.Value, ct);
        return Ok(PurchaseOrderResponse.From(order));
    }

    [HttpPost("purchase-orders/{id:guid}/cancel")]
    [LocalSqliteWrite]
    public async Task<ActionResult<PurchaseOrderResponse>> Cancel(
        Guid id, RevisionRequest request, CancellationToken ct)
    {
        var order = await service.CancelAsync(id, request.ExpectedRevision!.Value, ct);
        return Ok(PurchaseOrderResponse.From(order));
    }

    [HttpPost("purchase-orders/{id:guid}/receipts")]
    [LocalSqliteWrite]
    public async Task<ActionResult<PurchaseOrderReceiptResponse>> Receive(
        Guid id, ReceivePurchaseOrderRequest request, CancellationToken ct)
    {
        var actor = User.GetCustomerId();
        if (actor is null) return Unauthorized();

        var changes = request.Lines.Select(line =>
            new PurchaseOrderReceiptChange(line.PurchaseOrderLineId, line.Quantity)).ToArray();
        var result = await service.ReceiveAsync(
            id, request.OperationId, request.ExpectedRevision!.Value, actor.Value, changes, ct);
        var response = PurchaseOrderReceiptResponse.From(result.Receipt);
        return result.Replayed
            ? Ok(response)
            : CreatedAtAction(nameof(Get), new { id }, response);
    }
}
