using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record CartMergeResult(Cart Target, int SourceVersion);

public class CartMergeService(AgoraDbContext db, TimeProvider clock)
{
    public async Task<CartMergeResult> MergeAsync(Guid owner, string sourceToken, string targetToken,
        int expectedSourceVersion, int expectedTargetVersion, CancellationToken ct = default)
    {
        if (sourceToken == targetToken) throw new DomainException("Source and target carts must differ.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var carts = await db.Carts.Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v!.Product)
            .Include(c => c.Items).ThenInclude(i => i.ProductVariant).ThenInclude(v => v!.Inventory)
            .Where(c => c.Token == sourceToken || c.Token == targetToken).AsSplitQuery().ToListAsync(ct);
        var source = carts.SingleOrDefault(c => c.Token == sourceToken && (c.CustomerId == null || c.CustomerId == owner));
        var target = carts.SingleOrDefault(c => c.Token == targetToken && c.CustomerId == owner);
        if (source is null || target is null) throw new NotFoundException("An eligible source and owned target cart are required.");
        if (source.Version != expectedSourceVersion || target.Version != expectedTargetVersion)
            throw new CartMergeConflictException("A cart changed. Reload both versions before merging.");
        if (source.Items.Count == 0) throw new InvalidCartCombinationException([new(Guid.Empty, "", "The source cart is empty.")]);
        var proposed = CartCombinationRules.Combine(
            target.Items.Select(i => new ProposedCartLine(i.ProductVariantId, i.Quantity, i.IsSavedForLater)),
            source.Items.Select(i => new ProposedCartLine(i.ProductVariantId, i.Quantity, i.IsSavedForLater)));
        var variants = carts.SelectMany(c => c.Items).Select(i => i.ProductVariant).Where(v => v is not null)
            .GroupBy(v => v!.Id).ToDictionary(g => g.Key, g => g.First()!);
        var problems = CartCombinationRules.Validate(proposed, variants);
        if (problems.Count > 0) throw new InvalidCartCombinationException(problems);
        var now = clock.GetUtcNow();
        CartCombinationWriter.Apply(db, target, proposed, variants, now);
        source.ReplaceContents([], now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new CartMergeResult(target, source.Version);
    }
}

public sealed class CartMergeConflictException(string message) : DomainException(message);
