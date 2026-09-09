using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;

namespace Agora.Infrastructure.Services;

/// <summary>Applies already validated intent to a tracked cart and explicitly inserts new children.</summary>
internal static class CartCombinationWriter
{
    internal static void Apply(AgoraDbContext db, Cart cart, IReadOnlyList<ProposedCartLine> proposed,
        IReadOnlyDictionary<Guid, ProductVariant> trackedVariants, DateTimeOffset now)
    {
        var originalIds = cart.Items.Select(i => i.Id).ToHashSet();
        cart.ReplaceContents(proposed.Select(l => new CartLineState(l.VariantId, l.Quantity, l.IsSavedForLater)).ToArray(), now);
        foreach (var line in cart.Items)
        {
            line.ProductVariant = trackedVariants[line.ProductVariantId];
            // Generated IDs alone do not tell EF that a child is new after relationship discovery.
            if (!originalIds.Contains(line.Id)) db.CartItems.Add(line);
        }
    }
}
