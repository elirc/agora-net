using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

/// <summary>Resources owned by the authenticated customer.</summary>
[ApiController]
[Authorize]
[Route("api/me")]
public class MeController(AgoraDbContext db) : ControllerBase
{
    public const int MaxPageSize = 100;

    /// <summary>The customer's order history, newest first.</summary>
    [HttpGet("orders")]
    public async Task<ActionResult<PagedResult<OrderResponse>>> Orders(
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
        var query = db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt);

        var totalCount = await query.CountAsync(ct);
        var orders = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new PagedResult<OrderResponse>(
            orders.Select(OrderResponse.From).ToList(), page, pageSize, totalCount));
    }
}
