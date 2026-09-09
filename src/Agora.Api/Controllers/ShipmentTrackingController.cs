using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize, Agora.Api.Filters.LocalSqliteWrite]
[Route("api")]
public class ShipmentTrackingController(AgoraDbContext db, TimeProvider clock) : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpPost("admin/fulfillments/{id:guid}/tracking-events")]
    public async Task<ActionResult<AdminShipmentTrackingEventResponse>> Add(Guid id, AddShipmentTrackingRequest request, CancellationToken ct)
    {
        var actor = User.GetCustomerId(); if (actor is null) return Unauthorized();
        if (!QueryRules.TryNamedEnum<ShipmentTrackingStatus>(request.Status, out var status))
            return BadRequest(new ProblemDetails { Title = "Use a named shipment tracking status." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var fulfillment = await db.Fulfillments.SingleOrDefaultAsync(f => f.Id == id, ct);
        if (fulfillment is null) return NotFound();
        if (fulfillment.TrackingVersion != request.ExpectedVersion)
            return Conflict(new ProblemDetails { Title = "Shipment tracking changed. Reload its revision." });
        ShipmentTrackingEvent entry;
        try { entry = fulfillment.RecordTracking(status, request.Message, actor.Value, clock.GetUtcNow()); }
        catch (InvalidShipmentTrackingTransitionException error) { return Conflict(new ProblemDetails { Title = error.Message }); }
        db.ShipmentTrackingEvents.Add(entry); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return CreatedAtAction(nameof(Admin), new { id }, AdminResponse(entry));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin/fulfillments/{id:guid}/tracking-events")]
    public async Task<ActionResult<AdminShipmentTrackingHistoryResponse>> Admin(Guid id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!QueryRules.ValidPage(page, pageSize)) return BadRequest(new ProblemDetails { Title = "Invalid pagination." });
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var fulfillment = await db.Fulfillments.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, ct);
        if (fulfillment is null) return NotFound();
        var (events, count) = await Read(id, page, pageSize, ct); await transaction.CommitAsync(ct);
        return Ok(new AdminShipmentTrackingHistoryResponse(id, fulfillment.TrackingStatus.ToString(), fulfillment.TrackingVersion,
            new PagedResult<AdminShipmentTrackingEventResponse>(events.Select(AdminResponse).ToArray(), page, pageSize, count)));
    }

    [HttpGet("me/orders/{number}/fulfillments/{id:guid}/tracking-events")]
    public async Task<ActionResult<ShipmentTrackingHistoryResponse>> Mine(string number, Guid id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        if (!QueryRules.ValidPage(page, pageSize)) return BadRequest(new ProblemDetails { Title = "Invalid pagination." });
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var fulfillment = await db.Fulfillments.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id && f.Order!.Number == number && f.Order.CustomerId == owner, ct);
        if (fulfillment is null) return NotFound();
        var (events, count) = await Read(id, page, pageSize, ct); await transaction.CommitAsync(ct);
        return Ok(new ShipmentTrackingHistoryResponse(id, fulfillment.TrackingStatus.ToString(), fulfillment.TrackingVersion,
            new PagedResult<ShipmentTrackingEventResponse>(events.Select(e => new ShipmentTrackingEventResponse(
                e.Id, e.Sequence, e.Status.ToString(), e.Message, e.RecordedAt)).ToArray(), page, pageSize, count)));
    }

    private async Task<(List<ShipmentTrackingEvent> Events, int Count)> Read(Guid id, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ShipmentTrackingEvents.AsNoTracking().Where(e => e.FulfillmentId == id);
        var count = await query.CountAsync(ct);
        return (await query.OrderBy(e => e.Sequence).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct), count);
    }
    private static AdminShipmentTrackingEventResponse AdminResponse(ShipmentTrackingEvent e) =>
        new(e.Id, e.Sequence, e.Status.ToString(), e.Message, e.RecordedAt, e.ActorAdminId);
}
