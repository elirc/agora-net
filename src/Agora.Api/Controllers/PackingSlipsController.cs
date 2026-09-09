using Agora.Api.Rendering;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/orders/{number}/packing-slip")]
public class PackingSlipsController(AgoraDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string number, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.AsNoTracking().Where(o => o.Number == number)
            .Select(o => new { o.Id, o.Number, o.CreatedAt, o.Status,
                Address = new PackingSlipAddress(o.ShippingAddress.FullName, o.ShippingAddress.Line1,
                    o.ShippingAddress.Line2, o.ShippingAddress.City, o.ShippingAddress.Region,
                    o.ShippingAddress.PostalCode, o.ShippingAddress.Country) }).SingleOrDefaultAsync(ct);
        if (order is null) return NotFound();
        if (order.Status is not (OrderStatus.Paid or OrderStatus.PartiallyFulfilled or OrderStatus.Fulfilled))
            return Conflict(new ProblemDetails { Title = "Packing slips require a paid, partially fulfilled, or fulfilled order." });
        var lines = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == order.Id)
            .OrderBy(i => i.Sku).ThenBy(i => i.Id).Take(501)
            .Select(i => new { i.Id, i.Sku, i.ProductName, i.VariantName, i.Quantity }).ToListAsync(ct);
        if (lines.Count > 500)
            return UnprocessableEntity(new ProblemDetails { Title = "Packing slips support at most 500 order lines." });
        var ids = lines.Select(i => i.Id).ToArray();
        var shipped = await db.FulfillmentItems.AsNoTracking().Where(i => ids.Contains(i.OrderItemId))
            .GroupBy(i => i.OrderItemId).Select(g => new { Id = g.Key, Quantity = g.Sum(i => (long)i.Quantity) })
            .ToDictionaryAsync(i => i.Id, i => i.Quantity, ct);
        if (lines.Any(i => shipped.GetValueOrDefault(i.Id) < 0 || shipped.GetValueOrDefault(i.Id) > i.Quantity))
            return Conflict(new ProblemDetails { Title = "Order fulfillment quantities are inconsistent." });
        await transaction.CommitAsync(ct);
        var model = new PackingSlipModel(order.Number, order.CreatedAt, order.Address,
            lines.Select(i => new PackingSlipLine(i.Sku, i.ProductName, i.VariantName, i.Quantity,
                shipped.GetValueOrDefault(i.Id), i.Quantity - shipped.GetValueOrDefault(i.Id))).ToArray());
        return Content(PackingSlipRenderer.Render(model), "text/html; charset=utf-8");
    }
}
