using Agora.Api.Contracts;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/inventory")]
public class InventoryController(AgoraDbContext db) : ControllerBase
{
    [HttpGet("{sku}")]
    public async Task<ActionResult<InventoryResponse>> GetBySku(string sku, CancellationToken ct)
    {
        var variant = await db.ProductVariants
            .AsNoTracking()
            .Include(v => v.Inventory)
            .FirstOrDefaultAsync(v => v.Sku == sku, ct);

        return variant?.Inventory is null
            ? NotFound()
            : Ok(InventoryResponse.From(variant.Sku, variant.Inventory));
    }

    /// <summary>Sets the absolute on-hand stock level for a SKU.</summary>
    [Authorize(Roles = "Admin")]
    [HttpPut("{sku}")]
    public async Task<ActionResult<InventoryResponse>> SetStock(
        string sku, SetStockRequest request, CancellationToken ct)
    {
        var variant = await db.ProductVariants
            .Include(v => v.Inventory)
            .FirstOrDefaultAsync(v => v.Sku == sku, ct);

        if (variant?.Inventory is null)
        {
            return NotFound();
        }

        variant.Inventory.SetStock(request.QuantityOnHand); // throws if below reserved
        await db.SaveChangesAsync(ct);

        return Ok(InventoryResponse.From(variant.Sku, variant.Inventory));
    }
}
