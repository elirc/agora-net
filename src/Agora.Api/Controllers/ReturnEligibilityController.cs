using Agora.Api.Auth;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize]
[Route("api/me/orders/{number}/return-eligibility")]
public class ReturnEligibilityController(AgoraDbContext db, ReturnEligibilityService eligibility, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ReturnEligibilityResult>> Get(string number, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        var order = await db.Orders.AsNoTracking().Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Number == number && o.CustomerId == owner, ct);
        return order is null ? NotFound() : Ok(await eligibility.EvaluateAsync(order, clock.GetUtcNow(), ct));
    }
}
