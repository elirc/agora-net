using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record CartTemplateLineProblem(Guid? TemplateLineId, Guid VariantId, string Sku, string Reason);
public sealed class CartTemplateConflictException(string message) : DomainException(message);
public sealed class InvalidCartTemplateApplyException(IReadOnlyList<CartTemplateLineProblem> problems)
    : DomainException("The template cannot be applied to this cart.")
{
    public IReadOnlyList<CartTemplateLineProblem> Problems { get; } = problems;
}

public class CartTemplateService(AgoraDbContext db, TimeProvider clock)
{
    public async Task<CartTemplate> CreateAsync(Guid owner, string name, string cartToken, CancellationToken ct = default)
    {
        // SQLite's default write transaction serializes the capacity check and insertion.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var cartId = await db.Carts.Where(c => c.Token == cartToken && c.CustomerId == owner)
            .Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Owned cart not found.");
        if (await db.CartTemplates.CountAsync(t => t.CustomerId == owner, ct) >= 10)
            throw new CartTemplateConflictException("An account can store at most ten cart templates.");
        // Query the bounded children directly; a limited nested Include can require unsupported SQLite APPLY.
        var lines = await db.CartItems.AsNoTracking().Where(i => i.CartId == cartId && !i.IsSavedForLater)
            .OrderBy(i => i.Id).Take(51).Select(i => new CartTemplateSnapshot(
                i.ProductVariantId, i.Quantity, i.ProductVariant!.Sku, i.ProductVariant.Product!.Name, i.ProductVariant.Name)).ToArrayAsync(ct);
        var template = new CartTemplate(owner, name, lines, clock.GetUtcNow());
        db.CartTemplates.Add(template);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return template;
    }

    public async Task<Cart> ApplyAsync(Guid owner, Guid id, string targetToken, int expectedVersion, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var template = await db.CartTemplates.AsNoTracking().Include(t => t.Lines)
            .SingleOrDefaultAsync(t => t.Id == id && t.CustomerId == owner, ct)
            ?? throw new NotFoundException("Template not found.");
        var cart = await db.Carts.Include(c => c.Items).SingleOrDefaultAsync(c => c.Token == targetToken && c.CustomerId == owner, ct)
            ?? throw new NotFoundException("Owned target cart not found.");
        if (cart.Version != expectedVersion) throw new CartTemplateConflictException("The cart changed. Reload its version before applying.");
        try
        {
            var proposed = CartCombinationRules.Combine(cart.Items.Select(i => new ProposedCartLine(i.ProductVariantId, i.Quantity, i.IsSavedForLater)),
                template.Lines.Select(l => new ProposedCartLine(l.VariantId, l.Quantity, false)));
            var ids = proposed.Select(l => l.VariantId).ToArray();
            var variants = await db.ProductVariants.Include(v => v.Product).Include(v => v.Inventory)
                .Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
            var problems = CartCombinationRules.Validate(proposed, variants);
            if (problems.Count != 0) throw new InvalidCartCombinationException(problems);
            CartCombinationWriter.Apply(db, cart, proposed, variants, clock.GetUtcNow());
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return cart;
        }
        catch (InvalidCartCombinationException error)
        {
            var snapshots = template.Lines.ToDictionary(l => l.VariantId);
            throw new InvalidCartTemplateApplyException(error.Problems.Select(p =>
            {
                var line = snapshots.GetValueOrDefault(p.VariantId);
                return new CartTemplateLineProblem(line?.Id, p.VariantId, line?.Sku ?? p.Sku, p.Reason);
            }).ToArray());
        }
    }
}
