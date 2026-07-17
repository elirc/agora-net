using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

/// <summary>Resources owned by the authenticated customer.</summary>
[ApiController]
[Authorize]
[Route("api/me")]
public class MeController(AgoraDbContext db) : ControllerBase
{
    public const int MaxPageSize = 100;

    /// <summary>The customer's order history, newest first.</summary>
    [HttpGet("orders")]
    public async Task<ActionResult<PagedResult<OrderResponse>>> Orders(
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

        var customerId = User.GetCustomerId();
        var query = db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt);

        var totalCount = await query.CountAsync(ct);
        var orders = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new PagedResult<OrderResponse>(
            orders.Select(OrderResponse.From).ToList(), page, pageSize, totalCount));
    }

    /// <summary>The customer's address book, default first.</summary>
    [HttpGet("addresses")]
    public async Task<ActionResult<List<CustomerAddressResponse>>> Addresses(CancellationToken ct)
    {
        var customerId = User.GetCustomerId();
        var addresses = await db.CustomerAddresses
            .AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(ct);
        return Ok(addresses.Select(CustomerAddressResponse.From).ToList());
    }

    /// <summary>Saves an address; the customer's first address becomes the default.</summary>
    [HttpPost("addresses")]
    public async Task<ActionResult<CustomerAddressResponse>> AddAddress(
        SaveAddressRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var hasAny = await db.CustomerAddresses.AnyAsync(a => a.CustomerId == customerId, ct);

        var address = new CustomerAddress
        {
            CustomerId = customerId,
            Label = request.Label?.Trim() ?? string.Empty,
            Address = request.Address.ToAddress(),
            IsDefault = (request.IsDefault ?? false) || !hasAny,
        };

        if (address.IsDefault && hasAny)
        {
            await ClearDefaultAddressAsync(customerId, ct);
        }

        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Addresses), null, CustomerAddressResponse.From(address));
    }

    [HttpPut("addresses/{id:guid}")]
    public async Task<ActionResult<CustomerAddressResponse>> UpdateAddress(
        Guid id, SaveAddressRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var address = await db.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customerId, ct);
        if (address is null)
        {
            return NotFound();
        }

        if (request.IsDefault == true && !address.IsDefault)
        {
            await ClearDefaultAddressAsync(customerId, ct);
        }

        address.Label = request.Label?.Trim() ?? string.Empty;
        address.Address = request.Address.ToAddress();
        address.IsDefault = request.IsDefault ?? address.IsDefault;
        await db.SaveChangesAsync(ct);

        return Ok(CustomerAddressResponse.From(address));
    }

    [HttpDelete("addresses/{id:guid}")]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var address = await db.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customerId, ct);
        if (address is null)
        {
            return NotFound();
        }

        db.CustomerAddresses.Remove(address);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Makes the address the customer's default.</summary>
    [HttpPost("addresses/{id:guid}/default")]
    public async Task<ActionResult<CustomerAddressResponse>> SetDefaultAddress(
        Guid id, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var address = await db.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customerId, ct);
        if (address is null)
        {
            return NotFound();
        }

        await ClearDefaultAddressAsync(customerId, ct);
        address.IsDefault = true;
        await db.SaveChangesAsync(ct);

        return Ok(CustomerAddressResponse.From(address));
    }

    private async Task ClearDefaultAddressAsync(Guid customerId, CancellationToken ct)
    {
        var defaults = await db.CustomerAddresses
            .Where(a => a.CustomerId == customerId && a.IsDefault)
            .ToListAsync(ct);
        foreach (var existing in defaults)
        {
            existing.IsDefault = false;
        }
    }
}
