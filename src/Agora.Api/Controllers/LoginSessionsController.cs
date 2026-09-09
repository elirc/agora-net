using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Agora.Api.Controllers;

[ApiController, Authorize]
[Route("api/me/sessions")]
public sealed class LoginSessionsController(AgoraDbContext db, AuthenticationTimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<LoginSessionResponse>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!QueryRules.ValidPage(page, pageSize))
            return BadRequest(new ProblemDetails { Title = "Page must be positive and pageSize must be between 1 and 100." });
        Response.Headers.CacheControl = "private, no-store";
        var owner = User.GetCustomerId()!.Value;
        var current = CurrentSessionId();
        var now = clock.GetUtcNow();
        var query = db.Set<LoginSession>().AsNoTracking()
            .Where(s => s.CustomerId == owner && s.RevokedAt == null && s.ExpiresAt > now);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(s => s.IssuedAt).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new LoginSessionResponse(s.Id, s.DeviceLabel, s.IssuedAt, s.ExpiresAt,
                s.RevokedAt, s.Id == current)).ToArrayAsync(ct);
        return Ok(new PagedResult<LoginSessionResponse>(rows, page, pageSize, total));
    }

    [HttpDelete("{id:guid}")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var owner = User.GetCustomerId()!.Value;
        var session = await db.Set<LoginSession>()
            .SingleOrDefaultAsync(s => s.Id == id && s.CustomerId == owner, ct);
        if (session is null) return NotFound();
        session.Revoke(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("revoke-all")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<RevokeAllSessionsResponse>> RevokeAll(CancellationToken ct)
    {
        var owner = User.GetCustomerId()!.Value;
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var saved = await db.Set<LoginSession>()
            .Where(s => s.CustomerId == owner && s.RevokedAt == null).ToArrayAsync(ct);
        foreach (var session in saved) session.Revoke(now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new RevokeAllSessionsResponse(saved.Length, now));
    }

    private Guid? CurrentSessionId() =>
        Guid.TryParse(User.FindFirst("sid")?.Value, out var id) ? id : null;
}
