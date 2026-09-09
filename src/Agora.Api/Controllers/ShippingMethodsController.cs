using Agora.Api.Contracts;
using Agora.Api.Queries;
using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/shipping-methods")]
public class ShippingMethodsController(AgoraDbContext db) : ControllerBase
{
    /// <summary>Active shipping options shoppers can pick at checkout.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ShippingMethodResponse>>> List(
        [FromQuery, Range(0, 365)] int? maxDeliveryDays = null, CancellationToken ct = default)
    {
        var methods = await db.ShippingMethods
            .AsNoTracking()
            .Where(m => m.IsActive)
            .Where(m => !maxDeliveryDays.HasValue || m.MaxDays <= maxDeliveryDays.Value)
            .OrderBy(m => m.BaseRate)
            .ThenBy(m => m.Code)
            .ToListAsync(ct);
        return Ok(methods.Select(ShippingMethodResponse.From).ToList());
    }

    [HttpGet("{code}")]
    public async Task<ActionResult<ShippingMethodResponse>> GetByCode(string code, CancellationToken ct)
    {
        var method = await db.ShippingMethods.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Code == code.ToLowerInvariant(), ct);
        return method is null ? NotFound() : Ok(ShippingMethodResponse.From(method));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<ShippingMethodResponse>> Create(
        CreateShippingMethodRequest request, CancellationToken ct)
    {
        if (!QueryRules.TryNamedEnum<ShippingRateType>(request.RateType, out var rateType))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "RateType must be 'Flat' or 'Weighted'.",
            });
        }

        if (request.MinDays > request.MaxDays)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "MinDays cannot exceed MaxDays.",
            });
        }

        var code = request.Code.Trim().ToLowerInvariant();
        if (await db.ShippingMethods.AnyAsync(m => m.Code == code, ct))
        {
            return Conflict(new ProblemDetails
            {
                Title = $"Shipping method '{code}' already exists.",
            });
        }

        var method = new ShippingMethod
        {
            Code = code,
            Name = request.Name.Trim(),
            RateType = rateType,
            BaseRate = request.BaseRate,
            PerKgRate = request.PerKgRate,
            FreeThreshold = request.FreeThreshold,
            MinDays = request.MinDays,
            MaxDays = request.MaxDays,
            IsActive = request.IsActive ?? true,
            IsDefault = request.IsDefault ?? false,
        };

        if (method.IsDefault)
        {
            await ClearDefaultAsync(ct);
        }

        db.ShippingMethods.Add(method);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetByCode), new { code = method.Code },
            ShippingMethodResponse.From(method));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{code}")]
    public async Task<ActionResult<ShippingMethodResponse>> Update(
        string code, UpdateShippingMethodRequest request, CancellationToken ct)
    {
        if (!QueryRules.TryNamedEnum<ShippingRateType>(request.RateType, out var rateType))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "RateType must be 'Flat' or 'Weighted'.",
            });
        }

        if (request.MinDays > request.MaxDays)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "MinDays cannot exceed MaxDays.",
            });
        }

        var method = await db.ShippingMethods
            .FirstOrDefaultAsync(m => m.Code == code.ToLowerInvariant(), ct);
        if (method is null)
        {
            return NotFound();
        }

        if (request.IsDefault && !method.IsDefault)
        {
            await ClearDefaultAsync(ct);
        }

        method.Name = request.Name.Trim();
        method.RateType = rateType;
        method.BaseRate = request.BaseRate;
        method.PerKgRate = request.PerKgRate;
        method.FreeThreshold = request.FreeThreshold;
        method.MinDays = request.MinDays;
        method.MaxDays = request.MaxDays;
        method.IsActive = request.IsActive;
        method.IsDefault = request.IsDefault;
        await db.SaveChangesAsync(ct);

        return Ok(ShippingMethodResponse.From(method));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{code}")]
    public async Task<IActionResult> Delete(string code, CancellationToken ct)
    {
        var method = await db.ShippingMethods
            .FirstOrDefaultAsync(m => m.Code == code.ToLowerInvariant(), ct);
        if (method is null)
        {
            return NotFound();
        }

        db.ShippingMethods.Remove(method);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task ClearDefaultAsync(CancellationToken ct)
    {
        var defaults = await db.ShippingMethods.Where(m => m.IsDefault).ToListAsync(ct);
        foreach (var existing in defaults)
        {
            existing.IsDefault = false;
        }
    }
}
