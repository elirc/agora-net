using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Agora.Api.Filters.LocalSqliteWrite]
[Route("api/admin/integration-keys")]
public sealed class IntegrationKeysController(AgoraDbContext db, IntegrationKeyService keys, AuthenticationTimeProvider clock) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<IntegrationKeyCreatedResponse>> Create(CreateIntegrationKeyRequest request, CancellationToken ct)
    {
        var issue = await keys.IssueAsync(request.Name, request.Scopes, request.ExpiryDays, User.GetCustomerId()!.Value, ct);
        Response.Headers.CacheControl = "private, no-store";
        return StatusCode(201, new IntegrationKeyCreatedResponse(IntegrationKeyResponse.From(issue.Key), issue.Token));
    }
    [HttpGet]
    public async Task<ActionResult<PagedResult<IntegrationKeyResponse>>> List(CancellationToken ct, int page = 1, int pageSize = 20)
    {
        if (!QueryRules.ValidPage(page, pageSize)) return BadRequest(new ProblemDetails { Title = "Invalid page; maximum page size is 100." });
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var query = db.Set<IntegrationApiKey>().AsNoTracking();
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(k => k.CreatedAt).ThenBy(k => k.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(k => new { k.Id, k.Name, k.Scopes, k.ExpiresAt, k.RevokedAt }).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new PagedResult<IntegrationKeyResponse>(rows.Select(k => new IntegrationKeyResponse(k.Id, k.Name,
            Enum.GetValues<IntegrationKeyScope>().Where(s => k.Scopes.HasFlag(s)).Select(s => s.ToString()).ToArray(), k.ExpiresAt, k.RevokedAt)).ToArray(), page, pageSize, count));
    }
    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var key = await db.Set<IntegrationApiKey>().SingleOrDefaultAsync(k => k.Id == id, ct);
        if (key is null) return NotFound();
        key.Revoke(clock.GetUtcNow()); await db.SaveChangesAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(IntegrationKeyResponse.From(key));
    }
}
