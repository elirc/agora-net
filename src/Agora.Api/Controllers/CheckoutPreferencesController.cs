using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize, Agora.Api.Filters.LocalSqliteWrite]
[Route("api/me/checkout-preferences")]
public class CheckoutPreferencesController(AgoraDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CheckoutPreferenceResponse>> Get(CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        var preference = await db.CheckoutPreferences.AsNoTracking().SingleOrDefaultAsync(p => p.CustomerId == owner, ct);
        return Ok(new CheckoutPreferenceResponse(preference?.ShippingAddressId, preference?.ShippingMethodCode, preference?.Version));
    }

    [HttpPut]
    public async Task<ActionResult<CheckoutPreferenceResponse>> Put(PutCheckoutPreferenceRequest request, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Customers.AnyAsync(c => c.Id == owner, ct)) return NotFound();
        var preference = await db.CheckoutPreferences.SingleOrDefaultAsync(p => p.CustomerId == owner, ct);
        if (preference?.Version != request.ExpectedVersion)
            return Conflict(new ProblemDetails { Title = "Preferences changed. Reload their version before replacing them." });
        if (request.ShippingAddressId is { } addressId && !await db.CustomerAddresses.AnyAsync(a => a.Id == addressId && a.CustomerId == owner, ct))
            return UnprocessableEntity(new ProblemDetails { Title = "Select an address from your own address book." });
        var method = request.ShippingMethodCode?.Trim().ToLowerInvariant();
        if (method is not null && (method.Length == 0 || !await db.ShippingMethods.AnyAsync(m => m.Code == method && m.IsActive, ct)))
            return UnprocessableEntity(new ProblemDetails { Title = "Select an active shipping method, or null to clear it." });
        if (preference is null)
        {
            preference = new CheckoutPreference(owner.Value, request.ShippingAddressId, method);
            db.CheckoutPreferences.Add(preference);
        }
        else preference.Replace(request.ShippingAddressId, method);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Ok(new CheckoutPreferenceResponse(preference.ShippingAddressId, preference.ShippingMethodCode, preference.Version));
    }
}
