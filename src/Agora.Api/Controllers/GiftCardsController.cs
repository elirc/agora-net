using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/gift-cards")]
public class GiftCardsController(AgoraDbContext db) : ControllerBase
{
    public const int MaxPageSize = 100;

    /// <summary>Issues a new gift card (the generated code is the bearer secret).</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<GiftCardResponse>> Issue(
        IssueGiftCardRequest request, CancellationToken ct)
    {
        var card = new GiftCard(
            request.Amount, request.Currency ?? Money.DefaultCurrency, request.ExpiresAt);
        db.GiftCards.Add(card);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetByCode), new { code = card.Code },
            GiftCardResponse.From(card));
    }

    /// <summary>Balance check by code (the code itself is the credential).</summary>
    [HttpGet("{code}")]
    public async Task<ActionResult<GiftCardResponse>> GetByCode(string code, CancellationToken ct)
    {
        var card = await db.GiftCards.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Code == code.ToUpperInvariant(), ct);
        return card is null ? NotFound() : Ok(GiftCardResponse.From(card));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<ActionResult<PagedResult<GiftCardResponse>>> List(
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

        var query = db.GiftCards.AsNoTracking().OrderByDescending(g => g.CreatedAt);
        var totalCount = await query.CountAsync(ct);
        var cards = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return Ok(new PagedResult<GiftCardResponse>(
            cards.Select(GiftCardResponse.From).ToList(), page, pageSize, totalCount));
    }

    /// <summary>Deactivates a card so it can no longer be redeemed.</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("{code}/deactivate")]
    public async Task<ActionResult<GiftCardResponse>> Deactivate(string code, CancellationToken ct)
    {
        var card = await db.GiftCards
            .FirstOrDefaultAsync(g => g.Code == code.ToUpperInvariant(), ct);
        if (card is null)
        {
            return NotFound();
        }

        card.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Ok(GiftCardResponse.From(card));
    }
}
