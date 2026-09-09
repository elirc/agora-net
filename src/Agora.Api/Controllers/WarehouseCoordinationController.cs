using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Api.Filters;
using Agora.Domain.Entities;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Agora.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/orders/{number}")]
public sealed class WarehouseCoordinationController(
    OrderHoldService holds, WarehouseAssignmentService assignments, TimeProvider clock) : ControllerBase
{
    [HttpGet("holds")]
    public async Task<ActionResult<IReadOnlyList<OrderHoldResponse>>> Holds(string number, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        return Ok((await holds.ListAsync(number, ct)).Select(OrderHoldResponse.From));
    }

    [HttpPost("holds"), LocalSqliteWrite]
    public async Task<ActionResult<OrderHoldResponse>> Hold(
        string number, CreateOrderHoldRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var actor = User.GetCustomerId()!.Value;
        var named = Enum.GetNames<OrderHoldReason>().Any(name =>
            string.Equals(name, request.Reason, StringComparison.OrdinalIgnoreCase));
        if (!named || !Enum.TryParse<OrderHoldReason>(request.Reason, true, out var reason))
            return BadRequest(new ProblemDetails { Title = "Unknown hold reason." });

        var hold = await holds.CreateAsync(number, reason, request.Note, actor, ct);
        return CreatedAtAction(nameof(Holds), new { number }, OrderHoldResponse.From(hold));
    }

    [HttpPost("holds/{holdId:guid}/release"), LocalSqliteWrite]
    public async Task<ActionResult<OrderHoldResponse>> ReleaseHold(
        string number, Guid holdId, ReleaseOrderHoldRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var released = await holds.ReleaseAsync(number, holdId, request.ExpectedRevision!.Value,
            User.GetCustomerId()!.Value, ct);
        return Ok(OrderHoldResponse.From(released));
    }

    [HttpGet("work-assignment")]
    public async Task<ActionResult<WarehouseAssignmentResponse>> Assignment(string number, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var assignment = await assignments.ReadAsync(number, ct);
        return assignment is null ? NotFound() : Ok(WarehouseAssignmentResponse.From(assignment, clock.GetUtcNow()));
    }

    [HttpPost("work-assignment"), LocalSqliteWrite]
    public async Task<ActionResult<WarehouseAssignmentResponse>> Claim(string number, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var assignment = await assignments.ClaimAsync(number, User.GetCustomerId()!.Value, ct);
        return Ok(WarehouseAssignmentResponse.From(assignment, clock.GetUtcNow()));
    }

    [HttpPost("work-assignment/renew"), LocalSqliteWrite]
    public async Task<ActionResult<WarehouseAssignmentResponse>> Renew(
        string number, AssignmentCommandRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var assignment = await assignments.RenewAsync(number, request.AssignmentId,
            request.ExpectedRevision!.Value, User.GetCustomerId()!.Value, ct);
        return Ok(WarehouseAssignmentResponse.From(assignment, clock.GetUtcNow()));
    }

    [HttpPost("work-assignment/release"), LocalSqliteWrite]
    public async Task<ActionResult<AssignmentReleaseResponse>> ReleaseAssignment(
        string number, AssignmentCommandRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var released = await assignments.ReleaseAsync(number, request.AssignmentId,
            request.ExpectedRevision!.Value, User.GetCustomerId()!.Value, ct);
        return Ok(new AssignmentReleaseResponse(released.OrderId, released.AssignmentId,
            released.OwnerId, released.ReleasedAt));
    }
}
