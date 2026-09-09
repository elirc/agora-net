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
[Route("api")]
public class ReturnsController(AgoraDbContext db, ReturnService returnService, GuestOrderAccessService orderAccess) : ControllerBase
{
    public const int MaxPageSize = 100;

    /// <summary>
    /// Opens a return (RMA) for lines of a fulfilled order. Signed-in owners
    /// are matched by account; guests must supply the order email.
    /// </summary>
    [HttpPost("orders/{number}/returns")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<ReturnResponse>> Create(
        string number, CreateReturnRequestDto request, CancellationToken ct)
    {
        if (!Enum.TryParse<ReturnReason>(request.Reason, ignoreCase: true, out var reason))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Reason must be one of: Damaged, WrongItem, NotAsDescribed, NoLongerNeeded, Other.",
            });
        }

        var created = await returnService.CreateAsync(
            new CreateReturnInput(
                number,
                reason,
                request.Comment,
                request.Items.Select(i => new ReturnLineInput(i.OrderItemId, i.Quantity)).ToList(),
                Actor()),
            ct);

        return CreatedAtAction(nameof(GetByNumber), new { number = created.Number },
            ReturnResponse.From(await ReloadAsync(created.Id, ct)));
    }

    /// <summary>Return status tracking by RMA number.</summary>
    [HttpGet("returns/{number}", Name = "GetReturnByNumber")]
    public async Task<ActionResult<CustomerReturnResponse>> GetByNumber(string number, CancellationToken ct)
    {
        var request = await db.ReturnRequests
            .AsNoTracking()
            .Include(r => r.Items)
            .Include(r => r.Order)
            .FirstOrDefaultAsync(r => r.Number == number, ct);
        if (request is null) return NotFound();
        await orderAccess.EnsureCanReadAsync(request.Order!, Actor(), ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(CustomerReturnResponse.From(request));
    }

    /// <summary>The authenticated customer's returns, newest first.</summary>
    [Authorize]
    [HttpGet("me/returns")]
    public async Task<ActionResult<PagedResult<ReturnResponse>>> MyReturns(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            return BadRequest(new ProblemDetails
            {
                Title = $"page must be >= 1 and pageSize between 1 and {MaxPageSize}.",
            });
        }

        var customerId = User.GetCustomerId();
        var query = db.ReturnRequests
            .AsNoTracking()
            .Include(r => r.Items)
            .Include(r => r.Order)
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.CreatedAt);

        var totalCount = await query.CountAsync(ct);
        var requests = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return Ok(new PagedResult<ReturnResponse>(
            requests.Select(ReturnResponse.From).ToList(), page, pageSize, totalCount));
    }

    /// <summary>Admin queue, optionally filtered by status (default requested).</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("returns")]
    public async Task<ActionResult<PagedResult<ReturnResponse>>> List(
        [FromQuery] string? status = "requested",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            return BadRequest(new ProblemDetails
            {
                Title = $"page must be >= 1 and pageSize between 1 and {MaxPageSize}.",
            });
        }

        var query = db.ReturnRequests
            .AsNoTracking()
            .Include(r => r.Items)
            .Include(r => r.Order)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            if (!Enum.TryParse<ReturnStatus>(status, ignoreCase: true, out var parsed))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "status must be 'requested', 'approved', 'rejected', 'cancelled', or 'all'.",
                });
            }

            query = query.Where(r => r.Status == parsed);
        }

        var ordered = query.OrderBy(r => r.CreatedAt);
        var totalCount = await ordered.CountAsync(ct);
        var requests = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return Ok(new PagedResult<ReturnResponse>(
            requests.Select(ReturnResponse.From).ToList(), page, pageSize, totalCount));
    }

    /// <summary>Approves the RMA: refunds via the gateway and restocks the units.</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("returns/{number}/approve")]
    public async Task<ActionResult<ReturnResponse>> Approve(string number, CancellationToken ct) =>
        Ok(ReturnResponse.From(await returnService.ApproveAsync(number, ct)));

    [Authorize(Roles = "Admin")]
    [HttpPost("returns/{number}/reject")]
    public async Task<ActionResult<ReturnResponse>> Reject(
        string number, RejectReturnRequestDto request, CancellationToken ct) =>
        Ok(ReturnResponse.From(await returnService.RejectAsync(number, request.Note, ct)));

    /// <summary>Requester cancels an open RMA (account owner or order email).</summary>
    [HttpPost("returns/{number}/cancel")]
    public async Task<ActionResult<ReturnResponse>> Cancel(
        string number, CancelReturnRequestDto request, CancellationToken ct) =>
        Ok(ReturnResponse.From(
            await returnService.CancelAsync(number, Actor(), ct)));

    private async Task<ReturnRequest> ReloadAsync(Guid id, CancellationToken ct) =>
        await db.ReturnRequests
            .AsNoTracking()
            .Include(r => r.Items)
            .Include(r => r.Order)
            .FirstAsync(r => r.Id == id, ct);

    private OrderAccessActor Actor() => new(User.GetCustomerId(), User.IsInRole("Admin"),
        Request.Headers["X-Agora-Order-Access"].FirstOrDefault());
}
