using Agora.Api.Auth;
using Agora.Api.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agora.Api.Controllers;

[ApiController, Authorize]
[Route("api/me/orders/feed")]
public sealed class OrderHistoryFeedController(OrderHistoryFeedQuery query) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<OrderHistoryFeedResponse>> Get(CancellationToken ct, int limit = 25, string? cursor = null)
    {
        Response.Headers.CacheControl = "private, no-store";
        var owner = User.GetCustomerId();
        if (owner is null) return Unauthorized();
        try { return Ok(await query.ReadAsync(owner.Value, limit, cursor, ct)); }
        catch (InvalidOrderHistoryCursorException)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid order-history cursor or limit. Restart from the first page." });
        }
    }
}
