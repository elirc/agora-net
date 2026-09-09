using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/integrations")]
public sealed class IntegrationReadsController(AgoraDbContext db) : ControllerBase
{
    [HttpGet("catalog")]
    [Authorize(AuthenticationSchemes = IntegrationKeyAuthenticationHandler.SchemeName, Policy = IntegrationKeyAuthenticationHandler.CatalogPolicy)]
    public async Task<ActionResult<PagedResult<IntegrationCatalogRow>>> Catalog(CancellationToken ct, int page = 1, int pageSize = 20)
    {
        if (!QueryRules.ValidPage(page, pageSize)) return BadRequest(new ProblemDetails { Title = "Invalid page; maximum page size is 100." });
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var query = db.ProductVariants.AsNoTracking().Where(v => v.Product!.IsActive);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(v => v.Sku).ThenBy(v => v.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(v => new IntegrationCatalogRow(v.ProductId, v.Id, v.Product!.CategoryId, v.Product.Name, v.Product.Slug,
                v.Sku, v.Name, v.Price.Amount, v.Price.Currency, v.WeightGrams)).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new PagedResult<IntegrationCatalogRow>(rows, page, pageSize, count));
    }
    [HttpGet("inventory")]
    [Authorize(AuthenticationSchemes = IntegrationKeyAuthenticationHandler.SchemeName, Policy = IntegrationKeyAuthenticationHandler.InventoryPolicy)]
    public async Task<ActionResult<PagedResult<IntegrationInventoryRow>>> Inventory(CancellationToken ct, int page = 1, int pageSize = 20)
    {
        if (!QueryRules.ValidPage(page, pageSize)) return BadRequest(new ProblemDetails { Title = "Invalid page; maximum page size is 100." });
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var query = db.ProductVariants.AsNoTracking().Where(v => v.Inventory != null);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(v => v.Sku).ThenBy(v => v.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(v => new IntegrationInventoryRow(v.Id, v.Sku, v.Inventory!.QuantityOnHand, v.Inventory.QuantityReserved,
                v.Inventory.QuantityOnHand - v.Inventory.QuantityReserved, v.Inventory.Version)).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new PagedResult<IntegrationInventoryRow>(rows, page, pageSize, count));
    }
}
