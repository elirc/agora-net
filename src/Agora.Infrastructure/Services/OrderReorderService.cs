using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public class OrderReorderService(AgoraDbContext db, TimeProvider clock)
{
    public async Task<Cart> CreateAsync(Guid owner, string number, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.AsNoTracking().Where(o => o.Number == number && o.CustomerId == owner)
            .Select(o => new { o.Id, o.Status }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Owned order not found.");
        if (order.Status == OrderStatus.Pending) throw new InvalidOrderStateException("A pending order cannot be used for repeat purchase.");
        var groups = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == order.Id)
            .GroupBy(i => i.ProductVariantId).Select(g => new { VariantId = g.Key, Quantity = g.Sum(i => (long)i.Quantity), Sku = g.Min(i => i.Sku) })
            .OrderBy(g => g.VariantId).Take(51).ToListAsync(ct);
        if (groups.Count is < 1 or > 50)
            throw new InvalidCartCombinationException([new(Guid.Empty, "", "Repeat purchase requires 1–50 distinct variant lines.")]);
        var quantityProblems = groups.Where(g => g.Quantity is < 1 or > Cart.MaxQuantityPerLine)
            .Select(g => new CartLineProblem(g.VariantId, g.Sku ?? "", "Combined historical quantity must be between 1 and 99.")).ToArray();
        if (quantityProblems.Length > 0) throw new InvalidCartCombinationException(quantityProblems);
        var ids = groups.Select(g => g.VariantId).ToArray();
        var variants = await db.ProductVariants.AsNoTracking().Include(v => v.Product).Include(v => v.Inventory)
            .Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var proposed = groups.Select(g => new ProposedCartLine(g.VariantId, checked((int)g.Quantity), false)).ToArray();
        var problems = CartCombinationRules.Validate(proposed, variants, groups.ToDictionary(g => g.VariantId, g => g.Sku ?? ""));
        if (problems.Count > 0) throw new InvalidCartCombinationException(problems);
        var now = clock.GetUtcNow();
        var cart = new Cart { CustomerId = owner, CreatedAt = now, UpdatedAt = now };
        foreach (var line in proposed) cart.AddItem(line.VariantId, line.Quantity);
        cart.UpdatedAt = now;
        db.Carts.Add(cart);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        // Attach read models only after saving, so an untracked catalog graph cannot be inserted with the new cart.
        foreach (var line in cart.Items) line.ProductVariant = variants[line.ProductVariantId];
        return cart;
    }
}
