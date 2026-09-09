using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/reports/webhook-health")]
public class WebhookHealthController(AgoraDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WebhookHealthResponse>> Get([FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null, [FromQuery] Guid? subscriptionId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (from.HasValue != to.HasValue || !QueryRules.ValidPage(page, pageSize))
            return BadRequest(new ProblemDetails { Title = "Supply both dates or neither, and valid pagination." });
        var asOf = clock.GetUtcNow();
        var start = from ?? asOf.AddDays(-7);
        var end = to ?? asOf;
        if (start >= end || end - start > TimeSpan.FromDays(30))
            return BadRequest(new ProblemDetails { Title = "The interval must increase and span at most 30 days." });
        // Counts, page and existence check observe one database transaction, even while retries occur.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (subscriptionId is { } id && !await db.WebhookSubscriptions.AnyAsync(s => s.Id == id, ct)) return NotFound();
        var response = await WebhookHealthQuery.Read(db, asOf, start, end, subscriptionId, page, pageSize, ct);
        await transaction.CommitAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(response);
    }
}
