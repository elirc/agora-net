using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize(Roles = "Admin")]
[Route("api/admin/gift-cards/{id:guid}/transactions")]
public class GiftCardLedgerController(AgoraDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<GiftCardLedgerResponse>> Get(Guid id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!QueryRules.ValidPage(page, pageSize)) return BadRequest(new ProblemDetails { Title = "Invalid pagination." });
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var currency = await db.GiftCards.Where(g => g.Id == id).Select(g => g.Currency).SingleOrDefaultAsync(ct);
        if (currency is null) return NotFound();
        var query = db.GiftCardEntries.AsNoTracking().Where(e => e.GiftCardId == id);
        var first = await query.OrderBy(e => e.RecordedVersion).Select(e => new { e.Kind, e.RecordedVersion }).FirstOrDefaultAsync(ct);
        var count = await query.CountAsync(ct);
        var entries = await query.OrderBy(e => e.RecordedVersion).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new GiftCardLedgerResponse(id, currency, first?.Kind.ToString(), first?.RecordedVersion,
            new PagedResult<GiftCardEntryResponse>(entries.Select(GiftCardEntryResponse.From).ToArray(), page, pageSize, count)));
    }
}
