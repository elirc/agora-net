using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api")]
public class TaxController(AgoraDbContext db) : ControllerBase
{
    [HttpGet("tax-categories")]
    public async Task<ActionResult<List<TaxCategoryResponse>>> Categories(CancellationToken ct)
    {
        var categories = await db.TaxCategories.AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct);
        return Ok(categories.Select(TaxCategoryResponse.From).ToList());
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("tax-categories")]
    public async Task<ActionResult<TaxCategoryResponse>> CreateCategory(
        CreateTaxCategoryRequest request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToLowerInvariant();
        if (await db.TaxCategories.AnyAsync(c => c.Code == code, ct))
        {
            return Conflict(new ProblemDetails { Title = $"Tax category '{code}' already exists." });
        }

        var category = new TaxCategory { Code = code, Name = request.Name.Trim() };
        db.TaxCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Categories), null, TaxCategoryResponse.From(category));
    }

    [HttpGet("tax-zones")]
    public async Task<ActionResult<List<TaxZoneResponse>>> Zones(CancellationToken ct)
    {
        var zones = await db.TaxZones
            .AsNoTracking()
            .Include(z => z.Rates).ThenInclude(r => r.TaxCategory)
            .OrderBy(z => z.Code)
            .ToListAsync(ct);
        return Ok(zones.Select(TaxZoneResponse.From).ToList());
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("tax-zones")]
    public async Task<ActionResult<TaxZoneResponse>> CreateZone(
        SaveTaxZoneRequest request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToLowerInvariant();
        if (await db.TaxZones.AnyAsync(z => z.Code == code, ct))
        {
            return Conflict(new ProblemDetails { Title = $"Tax zone '{code}' already exists." });
        }

        var zone = new TaxZone
        {
            Code = code,
            Name = request.Name.Trim(),
            Country = request.Country.Trim().ToUpperInvariant(),
            Region = string.IsNullOrWhiteSpace(request.Region)
                ? null
                : request.Region.Trim().ToUpperInvariant(),
            DefaultRate = request.DefaultRate,
            IsActive = request.IsActive ?? true,
        };

        var rateResult = await ApplyRatesAsync(zone, request.Rates, ct);
        if (rateResult is not null)
        {
            return rateResult;
        }

        db.TaxZones.Add(zone);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Zones), null, await LoadZoneResponse(zone.Id, ct));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("tax-zones/{code}")]
    public async Task<ActionResult<TaxZoneResponse>> UpdateZone(
        string code, SaveTaxZoneRequest request, CancellationToken ct)
    {
        var zone = await db.TaxZones
            .Include(z => z.Rates)
            .FirstOrDefaultAsync(z => z.Code == code.ToLowerInvariant(), ct);
        if (zone is null)
        {
            return NotFound();
        }

        zone.Name = request.Name.Trim();
        zone.Country = request.Country.Trim().ToUpperInvariant();
        zone.Region = string.IsNullOrWhiteSpace(request.Region)
            ? null
            : request.Region.Trim().ToUpperInvariant();
        zone.DefaultRate = request.DefaultRate;
        zone.IsActive = request.IsActive ?? zone.IsActive;

        zone.Rates.Clear();
        var rateResult = await ApplyRatesAsync(zone, request.Rates, ct);
        if (rateResult is not null)
        {
            return rateResult;
        }

        await db.SaveChangesAsync(ct);
        return Ok(await LoadZoneResponse(zone.Id, ct));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("tax-zones/{code}")]
    public async Task<IActionResult> DeleteZone(string code, CancellationToken ct)
    {
        var zone = await db.TaxZones
            .FirstOrDefaultAsync(z => z.Code == code.ToLowerInvariant(), ct);
        if (zone is null)
        {
            return NotFound();
        }

        db.TaxZones.Remove(zone);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Resolves category codes to per-zone rate overrides; 422 on unknown codes.</summary>
    private async Task<ActionResult<TaxZoneResponse>?> ApplyRatesAsync(
        TaxZone zone, List<TaxZoneRateDto>? rates, CancellationToken ct)
    {
        foreach (var rate in rates ?? [])
        {
            var categoryCode = rate.TaxCategoryCode.Trim().ToLowerInvariant();
            var category = await db.TaxCategories
                .FirstOrDefaultAsync(c => c.Code == categoryCode, ct);
            if (category is null)
            {
                return UnprocessableEntity(new ProblemDetails
                {
                    Title = $"Tax category '{categoryCode}' does not exist.",
                });
            }

            zone.Rates.Add(new TaxZoneRate
            {
                TaxZoneId = zone.Id,
                TaxCategoryId = category.Id,
                Rate = rate.Rate,
            });
        }

        return null;
    }

    private async Task<TaxZoneResponse> LoadZoneResponse(Guid zoneId, CancellationToken ct) =>
        TaxZoneResponse.From(await db.TaxZones
            .AsNoTracking()
            .Include(z => z.Rates).ThenInclude(r => r.TaxCategory)
            .FirstAsync(z => z.Id == zoneId, ct));
}
