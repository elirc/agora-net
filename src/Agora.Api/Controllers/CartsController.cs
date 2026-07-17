using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/carts")]
public class CartsController(AgoraDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CartResponse>> Create(CancellationToken ct)
    {
        // Signed-in shoppers get their cart attached; guests keep the token-only flow.
        var cart = new Cart { CustomerId = User.GetCustomerId() };
        db.Carts.Add(cart);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetByToken), new { token = cart.Token }, CartResponse.From(cart));
    }

    /// <summary>Attaches a guest cart to the authenticated customer's account.</summary>
    [Authorize]
    [HttpPost("{token}/claim")]
    public async Task<ActionResult<CartResponse>> Claim(string token, CancellationToken ct)
    {
        var cart = await LoadCart(token, tracking: true, ct);
        if (cart is null)
        {
            return NotFound();
        }

        var customerId = User.GetCustomerId()!.Value;
        if (cart.CustomerId is { } owner && owner != customerId)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Cart already belongs to another customer.",
            });
        }

        cart.CustomerId = customerId;
        await db.SaveChangesAsync(ct);
        return Ok(CartResponse.From(cart));
    }

    [HttpGet("{token}")]
    public async Task<ActionResult<CartResponse>> GetByToken(string token, CancellationToken ct)
    {
        var cart = await LoadCart(token, tracking: false, ct);
        return cart is null ? NotFound() : Ok(CartResponse.From(cart));
    }

    [HttpPost("{token}/items")]
    public async Task<ActionResult<CartResponse>> AddItem(
        string token, AddCartItemRequest request, CancellationToken ct)
    {
        var cart = await LoadCart(token, tracking: true, ct);
        if (cart is null)
        {
            return NotFound();
        }

        var variant = await db.ProductVariants
            .Include(v => v.Product)
            .Include(v => v.Inventory)
            .FirstOrDefaultAsync(v => v.Id == request.ProductVariantId, ct);
        if (variant is null)
        {
            return UnprocessableEntity(new ProblemDetails { Title = "Product variant does not exist." });
        }

        if (variant.Product is { IsActive: false })
        {
            return UnprocessableEntity(new ProblemDetails { Title = "Product is not available for sale." });
        }

        var item = cart.AddItem(variant.Id, request.Quantity); // enforces quantity rules

        // A brand-new line has a client-generated key; EF discovers it via the
        // navigation and would otherwise assume it already exists (Modified).
        if (db.Entry(item).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
        {
            db.CartItems.Add(item);
        }

        var available = variant.Inventory?.QuantityAvailable ?? 0;
        if (item.Quantity > available)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Insufficient stock",
                Detail = $"Requested {item.Quantity} of '{variant.Sku}' but only {available} available.",
            });
        }

        await db.SaveChangesAsync(ct);
        return Ok(CartResponse.From(cart));
    }

    [HttpPut("{token}/items/{itemId:guid}")]
    public async Task<ActionResult<CartResponse>> UpdateItem(
        string token, Guid itemId, UpdateCartItemRequest request, CancellationToken ct)
    {
        var cart = await LoadCart(token, tracking: true, ct);
        if (cart is null)
        {
            return NotFound();
        }

        if (request.Quantity > 0)
        {
            var item = cart.Items.FirstOrDefault(i => i.Id == itemId);
            var available = item?.ProductVariant?.Inventory?.QuantityAvailable ?? 0;
            if (item is not null && request.Quantity > available)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Insufficient stock",
                    Detail = $"Requested {request.Quantity} but only {available} available.",
                });
            }
        }

        cart.UpdateItemQuantity(itemId, request.Quantity); // 404s via NotFoundException
        await db.SaveChangesAsync(ct);
        return Ok(CartResponse.From(cart));
    }

    [HttpDelete("{token}/items/{itemId:guid}")]
    public async Task<ActionResult<CartResponse>> RemoveItem(string token, Guid itemId, CancellationToken ct)
    {
        var cart = await LoadCart(token, tracking: true, ct);
        if (cart is null)
        {
            return NotFound();
        }

        cart.RemoveItem(itemId);
        await db.SaveChangesAsync(ct);
        return Ok(CartResponse.From(cart));
    }

    [HttpDelete("{token}")]
    public async Task<IActionResult> Clear(string token, CancellationToken ct)
    {
        var cart = await LoadCart(token, tracking: true, ct);
        if (cart is null)
        {
            return NotFound();
        }

        cart.Clear();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<Cart?> LoadCart(string token, bool tracking, CancellationToken ct)
    {
        var query = db.Carts
            .Include(c => c.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Product)
            .Include(c => c.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Inventory)
            .AsQueryable();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(c => c.Token == token, ct);
    }
}
