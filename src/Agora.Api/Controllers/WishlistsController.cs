using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Api.Queries;
using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

/// <summary>
/// The authenticated customer's wishlists. A default list is created on first
/// use; additional named lists can be added. Reading a list refreshes each
/// item's out-of-stock observation so "back in stock" can be flagged later.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me/wishlists")]
public class WishlistsController(AgoraDbContext db, Agora.Api.Queries.CartResponseFactory responses) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<WishlistSummaryResponse>>> List(
        [FromQuery, MaxLength(100)] string? search = null, CancellationToken ct = default)
    {
        var customerId = User.GetCustomerId()!.Value;
        await GetOrCreateDefaultAsync(customerId, ct);

        var query = db.Wishlists
            .AsNoTracking()
            .Where(w => w.CustomerId == customerId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = QueryRules.LiteralContains(search);
            query = query.Where(w => EF.Functions.Like(w.Name, pattern, "\\"));
        }
        var summaries = await query
            .OrderByDescending(w => w.IsDefault)
            .ThenBy(w => w.CreatedAt)
            .ThenBy(w => w.Id)
            .Select(w => new { Wishlist = w, ItemCount = w.Items.Count })
            .ToListAsync(ct);

        return Ok(summaries
            .Select(s => WishlistSummaryResponse.From(s.Wishlist, s.ItemCount))
            .ToList());
    }

    [HttpPost]
    public async Task<ActionResult<WishlistResponse>> Create(
        CreateWishlistRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var name = request.Name.Trim();

        if (await db.Wishlists.AnyAsync(w => w.CustomerId == customerId && w.Name == name, ct))
        {
            return Conflict(new ProblemDetails
            {
                Title = $"A wishlist named '{name}' already exists.",
            });
        }

        var wishlist = new Wishlist { CustomerId = customerId, Name = name };
        db.Wishlists.Add(wishlist);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = wishlist.Id },
            WishlistResponse.From(wishlist));
    }

    /// <summary>The customer's default wishlist (created on first use).</summary>
    [HttpGet("default")]
    public async Task<ActionResult<WishlistResponse>> GetDefault(CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var wishlist = await GetOrCreateDefaultAsync(customerId, ct);
        return Ok(await ToResponseWithObservationAsync(wishlist.Id, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WishlistResponse>> GetById(Guid id, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        if (!await db.Wishlists.AnyAsync(w => w.Id == id && w.CustomerId == customerId, ct))
        {
            return NotFound();
        }

        return Ok(await ToResponseWithObservationAsync(id, ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WishlistResponse>> Rename(
        Guid id, CreateWishlistRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var wishlist = await LoadAsync(id, customerId, ct);
        if (wishlist is null)
        {
            return NotFound();
        }

        var name = request.Name.Trim();
        if (await db.Wishlists.AnyAsync(
                w => w.CustomerId == customerId && w.Name == name && w.Id != id, ct))
        {
            return Conflict(new ProblemDetails
            {
                Title = $"A wishlist named '{name}' already exists.",
            });
        }

        wishlist.Name = name;
        await db.SaveChangesAsync(ct);
        return Ok(WishlistResponse.From(wishlist));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var wishlist = await db.Wishlists
            .FirstOrDefaultAsync(w => w.Id == id && w.CustomerId == customerId, ct);
        if (wishlist is null)
        {
            return NotFound();
        }

        if (wishlist.IsDefault)
        {
            return Conflict(new ProblemDetails
            {
                Title = "The default wishlist cannot be deleted.",
            });
        }

        db.Wishlists.Remove(wishlist);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<WishlistResponse>> AddItem(
        Guid id, AddWishlistItemRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var wishlist = await LoadAsync(id, customerId, ct);
        if (wishlist is null)
        {
            return NotFound();
        }

        return await AddItemCore(wishlist, request, ct);
    }

    /// <summary>Adds to the default wishlist, creating it if needed.</summary>
    [HttpPost("default/items")]
    public async Task<ActionResult<WishlistResponse>> AddItemToDefault(
        AddWishlistItemRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var wishlist = await GetOrCreateDefaultAsync(customerId, ct);
        await db.Entry(wishlist).Collection(w => w.Items).LoadAsync(ct);
        return await AddItemCore(wishlist, request, ct);
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<WishlistResponse>> RemoveItem(
        Guid id, Guid itemId, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var wishlist = await LoadAsync(id, customerId, ct);
        if (wishlist is null)
        {
            return NotFound();
        }

        var item = wishlist.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return NotFound();
        }

        wishlist.Items.Remove(item);
        wishlist.MembershipChanged();
        await db.SaveChangesAsync(ct);
        return Ok(await ToResponseWithObservationAsync(id, ct));
    }

    [HttpDelete("{id:guid}/items")]
    public async Task<IActionResult> ClearItems(Guid id, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var wishlist = await db.Wishlists.Include(w => w.Items)
            .FirstOrDefaultAsync(w => w.Id == id && w.CustomerId == customerId, ct);
        if (wishlist is null) return NotFound();
        if (wishlist.Items.Count > 0) wishlist.MembershipChanged();
        db.WishlistItems.RemoveRange(wishlist.Items);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Moves a wishlist item into a cart (quantity 1) and off the wishlist.</summary>
    [HttpPost("{id:guid}/items/{itemId:guid}/move-to-cart")]
    public async Task<ActionResult<CartResponse>> MoveToCart(
        Guid id, Guid itemId, MoveWishlistItemToCartRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var wishlist = await LoadAsync(id, customerId, ct);
        var item = wishlist?.Items.FirstOrDefault(i => i.Id == itemId);
        if (wishlist is null || item is null)
        {
            return NotFound();
        }

        var cart = await db.Carts
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v!.Product)
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v!.Inventory)
            .FirstOrDefaultAsync(c => c.Token == request.CartToken, ct);
        if (cart is null || (cart.CustomerId is { } owner && owner != customerId))
        {
            return NotFound();
        }

        var variant = item.ProductVariant!;
        var cartLine = cart.AddItem(variant.Id, 1);
        if (db.Entry(cartLine).State == EntityState.Detached)
        {
            db.CartItems.Add(cartLine);
        }

        var available = variant.Inventory?.QuantityAvailable ?? 0;
        if (cartLine.Quantity > available)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Insufficient stock",
                Detail = $"Requested {cartLine.Quantity} of '{variant.Sku}' but only {available} available.",
            });
        }

        wishlist.Items.Remove(item);
        wishlist.MembershipChanged();
        await db.SaveChangesAsync(ct);

        // The moved line's variant/product may not be in the cart include graph yet.
        cartLine.ProductVariant ??= variant;
        return Ok(await responses.CreateAsync(cart, ct));
    }

    [HttpPut("{id:guid}/items/{itemId:guid}/note")]
    public async Task<ActionResult<WishlistNoteResponse>> EditNote(
        Guid id, Guid itemId, EditWishlistNoteRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var item = await db.WishlistItems.FirstOrDefaultAsync(i => i.Id == itemId
            && i.WishlistId == id && i.Wishlist!.CustomerId == customerId, ct);
        if (item is null) return NotFound();
        if (item.NoteVersion != request.ExpectedVersion) return Conflict(new ProblemDetails { Title = "The note has changed. Reload before editing." });
        item.EditNote(request.Note);
        await db.SaveChangesAsync(ct);
        return Ok(new WishlistNoteResponse(item.Id, item.Note, item.NoteVersion));
    }

    [HttpPost("{id:guid}/copy-items")]
    public async Task<ActionResult<WishlistCopyResponse>> CopyItems(
        Guid id, CopyWishlistItemsRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var target = await LoadAsync(id, customerId, ct);
        var source = await LoadAsync(request.SourceId, customerId, ct);
        if (target is null || source is null) return NotFound();
        if (id == request.SourceId) return UnprocessableEntity(new ProblemDetails { Title = "Source and target must differ." });
        if (target.MembershipVersion != request.ExpectedTargetVersion)
            return Conflict(new ProblemDetails { Title = "Target membership has changed. Reload before copying." });
        var byId = source.Items.ToDictionary(i => i.Id);
        if (request.ItemIds.Any(itemId => !byId.ContainsKey(itemId)))
            return UnprocessableEntity(new ProblemDetails { Title = "Every selected item must belong to the source wishlist." });
        var existing = target.Items.Select(i => i.ProductVariantId).ToHashSet();
        var added = new List<Guid>();
        var skipped = new List<Guid>();
        foreach (var itemId in request.ItemIds)
        {
            var original = byId[itemId];
            if (!existing.Add(original.ProductVariantId)) { skipped.Add(original.ProductVariantId); continue; }
            var item = target.AddItem(original.ProductVariantId, (original.ProductVariant?.Inventory?.QuantityAvailable ?? 0) <= 0);
            db.WishlistItems.Add(item);
            added.Add(item.ProductVariantId);
        }
        // SaveChanges atomically commits child inserts and the parent's conditional revision update.
        // A no-op also checks its revision against a competing membership edit, without advancing it.
        if (added.Count == 0) db.Entry(target).Property(w => w.MembershipVersion).IsModified = true;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            return Conflict(new ProblemDetails { Title = "Target membership changed while copying. Reload before retrying." });
        }
        return Ok(new WishlistCopyResponse(added, skipped, target.MembershipVersion));
    }

    private async Task<ActionResult<WishlistResponse>> AddItemCore(
        Wishlist wishlist, AddWishlistItemRequest request, CancellationToken ct)
    {
        var variant = await db.ProductVariants
            .Include(v => v.Inventory)
            .FirstOrDefaultAsync(v => v.Id == request.ProductVariantId, ct);
        if (variant is null)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Product variant does not exist.",
            });
        }

        if (wishlist.Items.Any(i => i.ProductVariantId == variant.Id))
        {
            return Conflict(new ProblemDetails
            {
                Title = "This item is already on the wishlist.",
            });
        }

        var outOfStock = (variant.Inventory?.QuantityAvailable ?? 0) <= 0;
        var item = wishlist.AddItem(variant.Id, outOfStock);
        db.WishlistItems.Add(item);
        await db.SaveChangesAsync(ct);

        return Ok(await ToResponseWithObservationAsync(wishlist.Id, ct));
    }

    private Task<Wishlist?> LoadAsync(Guid id, Guid customerId, CancellationToken ct) =>
        db.Wishlists
            .Include(w => w.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v!.Product)
            .Include(w => w.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v!.Inventory)
            .FirstOrDefaultAsync(w => w.Id == id && w.CustomerId == customerId, ct);

    private async Task<Wishlist> GetOrCreateDefaultAsync(Guid customerId, CancellationToken ct)
    {
        var existing = await db.Wishlists
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.IsDefault, ct);
        if (existing is not null)
        {
            return existing;
        }

        var wishlist = new Wishlist
        {
            CustomerId = customerId,
            Name = Wishlist.DefaultName,
            IsDefault = true,
        };
        db.Wishlists.Add(wishlist);
        await db.SaveChangesAsync(ct);
        return wishlist;
    }

    /// <summary>
    /// Loads the wishlist for the response and records any out-of-stock
    /// observations so a later restock can be flagged as "back in stock".
    /// </summary>
    private async Task<WishlistResponse> ToResponseWithObservationAsync(
        Guid wishlistId, CancellationToken ct)
    {
        var wishlist = await db.Wishlists
            .Include(w => w.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v!.Product)
            .Include(w => w.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v!.Inventory)
            .FirstAsync(w => w.Id == wishlistId, ct);

        var observed = false;
        foreach (var item in wishlist.Items)
        {
            var available = item.ProductVariant?.Inventory?.QuantityAvailable ?? 0;
            if (available <= 0 && !item.OutOfStockObserved)
            {
                item.OutOfStockObserved = true;
                observed = true;
            }
        }

        if (observed)
        {
            await db.SaveChangesAsync(ct);
        }

        return WishlistResponse.From(wishlist);
    }
}
