using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin")]
public class WebhookOperationsController(AgoraDbContext db, WebhookReplayService replays) : ControllerBase
{
    [HttpGet("webhook-deliveries/{id:guid}/attempts")]
    public async Task<ActionResult<WebhookAttemptHistoryResponse>> Attempts(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100) return BadRequest(new ProblemDetails { Title = "Invalid pagination." });
        Response.Headers.CacheControl = "private, no-store";
        var delivery = await db.WebhookDeliveries.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct); if (delivery is null) return NotFound();
        var query = db.Set<WebhookAttempt>().AsNoTracking().Where(a => a.DeliveryId == id).OrderBy(a => a.AttemptNumber);
        var count = await query.CountAsync(ct); var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new WebhookAttemptHistoryResponse(delivery.HistoryStartsAtAttempt,
            new(rows.Select(WebhookAttemptResponse.From).ToArray(), page, pageSize, count)));
    }
    [HttpPost("webhook-replays"), Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<WebhookReplayResponse>> Replay(CreateWebhookReplayRequest request, CancellationToken ct)
    {
        var batch = await replays.ReplayAsync(request.OperationId, request.SubscriptionId, request.EventIds,
            User.GetCustomerId() ?? Guid.Empty, ct);
        return Accepted(WebhookReplayResponse.From(batch));
    }
}
