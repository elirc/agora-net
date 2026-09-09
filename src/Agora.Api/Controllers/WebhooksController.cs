using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/webhooks")]
public class WebhooksController(AgoraDbContext db, WebhookService webhookService, TimeProvider clock) : ControllerBase
{
    public const int MaxPageSize = 100;

    [HttpGet]
    public async Task<ActionResult<PagedResult<WebhookSubscriptionResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            return BadRequest(new ProblemDetails
            {
                Title = $"page must be >= 1 and pageSize between 1 and {MaxPageSize}.",
            });
        }

        var query = db.WebhookSubscriptions.AsNoTracking().Where(s => !s.IsDeleted).OrderBy(s => s.CreatedAt);
        var totalCount = await query.CountAsync(ct);
        var subscriptions = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return Ok(new PagedResult<WebhookSubscriptionResponse>(
            subscriptions.Select(WebhookSubscriptionResponse.From).ToList(),
            page, pageSize, totalCount));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WebhookSubscriptionResponse>> GetById(Guid id, CancellationToken ct)
    {
        var subscription = await db.WebhookSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct);
        return subscription is null ? NotFound() : Ok(WebhookSubscriptionResponse.From(subscription));
    }

    [HttpPost]
    public async Task<ActionResult<WebhookSubscriptionResponse>> Create(
        SaveWebhookSubscriptionRequest request, CancellationToken ct)
    {
        var (events, error) = NormalizeEvents(request.Events);
        if (error is not null)
        {
            return error;
        }

        var subscription = new WebhookSubscription
        {
            Url = request.Url.Trim(),
            Secret = request.Secret,
            Events = events!,
            IsActive = request.IsActive ?? true,
        };
        db.WebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = subscription.Id },
            WebhookSubscriptionResponse.From(subscription));
    }

    [HttpPut("{id:guid}")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<WebhookSubscriptionResponse>> Update(
        Guid id, SaveWebhookSubscriptionRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var subscription = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct);
        if (subscription is null)
        {
            return NotFound();
        }

        var (events, error) = NormalizeEvents(request.Events);
        if (error is not null)
        {
            return error;
        }

        subscription.Url = request.Url.Trim();
        subscription.Secret = request.Secret;
        subscription.Events = events!;
        subscription.IsActive = request.IsActive ?? subscription.IsActive;
        if (!subscription.IsActive)
        {
            var queued = await db.WebhookDeliveries.Where(d => d.SubscriptionId == id &&
                (d.Status == WebhookDeliveryStatus.Pending || d.Status == WebhookDeliveryStatus.Failed)).ToListAsync(ct);
            foreach (var delivery in queued) delivery.Cancel();
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Ok(WebhookSubscriptionResponse.From(subscription));
    }

    [HttpDelete("{id:guid}")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var subscription = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subscription is null)
        {
            return NotFound();
        }

        subscription.SoftDelete(clock.GetUtcNow());
        var queued = await db.WebhookDeliveries.Where(d => d.SubscriptionId == id &&
            (d.Status == WebhookDeliveryStatus.Pending || d.Status == WebhookDeliveryStatus.Failed)).ToListAsync(ct);
        foreach (var delivery in queued) delivery.Cancel();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    /// <summary>The subscription's delivery log, newest first.</summary>
    [HttpGet("{id:guid}/deliveries")]
    public async Task<ActionResult<PagedResult<WebhookDeliveryResponse>>> Deliveries(
        Guid id,
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

        if (!await db.WebhookSubscriptions.AnyAsync(s => s.Id == id, ct))
        {
            return NotFound();
        }

        var query = db.WebhookDeliveries
            .AsNoTracking()
            .Where(d => d.SubscriptionId == id)
            .OrderByDescending(d => d.CreatedAt);

        var totalCount = await query.CountAsync(ct);
        var deliveries = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return Ok(new PagedResult<WebhookDeliveryResponse>(
            deliveries.Select(WebhookDeliveryResponse.From).ToList(), page, pageSize, totalCount));
    }

    /// <summary>Re-attempts a failed delivery.</summary>
    [HttpPost("deliveries/{id:guid}/retry")]
    public async Task<ActionResult<WebhookDeliveryResponse>> Retry(Guid id, CancellationToken ct) =>
        Accepted(WebhookDeliveryResponse.From(await webhookService.RetryAsync(id, ct)));

    private (List<string>? Events, ObjectResult? Error) NormalizeEvents(List<string> requested)
    {
        var events = requested
            .Select(e => e.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        var unknown = events.Where(e => !WebhookEvents.IsKnown(e)).ToList();
        if (unknown.Count > 0)
        {
            return (null, UnprocessableEntity(new ProblemDetails
            {
                Title = $"Unknown event(s): {string.Join(", ", unknown)}. " +
                        $"Known events: {string.Join(", ", WebhookEvents.All)}.",
            }));
        }

        return (events, null);
    }
}
