using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/discounts")]
public class DiscountsController(AgoraDbContext db) : ControllerBase
{
    public const int MaxPageSize = 100;

    [HttpGet]
    public async Task<ActionResult<PagedResult<DiscountResponse>>> List(
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

        var query = db.DiscountCodes.AsNoTracking().OrderBy(d => d.Code);
        var totalCount = await query.CountAsync(ct);
        var discounts = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return Ok(new PagedResult<DiscountResponse>(
            discounts.Select(DiscountResponse.From).ToList(), page, pageSize, totalCount));
    }

    [HttpGet("{code}")]
    public async Task<ActionResult<DiscountResponse>> GetByCode(string code, CancellationToken ct)
    {
        var discount = await db.DiscountCodes.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Code == code, ct);
        return discount is null ? NotFound() : Ok(DiscountResponse.From(discount));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<DiscountResponse>> Create(CreateDiscountRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<DiscountType>(request.Type, ignoreCase: true, out var type))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Type must be 'Percentage' or 'FixedAmount'.",
            });
        }

        if (type == DiscountType.Percentage && request.Value > 100m)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Percentage discounts cannot exceed 100.",
            });
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.DiscountCodes.AnyAsync(d => d.Code == code, ct))
        {
            return Conflict(new ProblemDetails { Title = $"Discount code '{code}' already exists." });
        }

        var discount = new DiscountCode
        {
            Code = code,
            Type = type,
            Value = request.Value,
            Currency = request.Currency?.ToUpperInvariant() ?? Money.DefaultCurrency,
            ExpiresAt = request.ExpiresAt,
            UsageLimit = request.UsageLimit,
            IsActive = request.IsActive ?? true,
        };
        db.DiscountCodes.Add(discount);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetByCode), new { code = discount.Code }, DiscountResponse.From(discount));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{code}")]
    public async Task<ActionResult<DiscountResponse>> Update(
        string code, UpdateDiscountRequest request, CancellationToken ct)
    {
        var discount = await db.DiscountCodes.FirstOrDefaultAsync(d => d.Code == code, ct);
        if (discount is null)
        {
            return NotFound();
        }

        discount.ExpiresAt = request.ExpiresAt;
        discount.UsageLimit = request.UsageLimit;
        discount.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);

        return Ok(DiscountResponse.From(discount));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{code}")]
    public async Task<IActionResult> Delete(string code, CancellationToken ct)
    {
        var discount = await db.DiscountCodes.FirstOrDefaultAsync(d => d.Code == code, ct);
        if (discount is null)
        {
            return NotFound();
        }

        db.DiscountCodes.Remove(discount);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
