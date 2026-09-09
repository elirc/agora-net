using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Domain.Services;

public sealed record ProposedCartLine(Guid VariantId, int Quantity, bool IsSavedForLater);
public sealed record CartLineProblem(Guid VariantId, string Sku, string Reason);

/// <summary>Pure composition and current-catalog validation shared by account shopping workflows.</summary>
public static class CartCombinationRules
{
    public static IReadOnlyList<ProposedCartLine> Combine(IEnumerable<ProposedCartLine> target, IEnumerable<ProposedCartLine> additions)
    {
        return target.Concat(additions).GroupBy(l => l.VariantId).Select(g =>
        {
            var quantity = g.Sum(l => (long)l.Quantity);
            if (quantity is < 1 or > Cart.MaxQuantityPerLine)
                throw new InvalidCartCombinationException([new CartLineProblem(g.Key, "", "Quantity must be between 1 and 99 after combining.")]);
            return new ProposedCartLine(g.Key, checked((int)quantity), g.All(l => l.IsSavedForLater));
        }).ToArray();
    }

    public static IReadOnlyList<CartLineProblem> Validate(IReadOnlyList<ProposedCartLine> proposed,
        IReadOnlyDictionary<Guid, ProductVariant> variants, IReadOnlyDictionary<Guid, string>? historicalSkus = null)
    {
        var problems = new List<CartLineProblem>();
        var proposedIds = proposed.Select(p => p.VariantId).ToHashSet();
        var currencies = variants.Values.Where(v => proposedIds.Contains(v.Id))
            .Select(v => v.Price.Currency).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var line in proposed)
        {
            var fallback = historicalSkus?.GetValueOrDefault(line.VariantId) ?? "";
            if (!variants.TryGetValue(line.VariantId, out var variant))
            { problems.Add(new(line.VariantId, fallback, "Variant no longer exists.")); continue; }
            var sku = historicalSkus is null ? variant.Sku : fallback;
            if (line.Quantity is < 1 or > Cart.MaxQuantityPerLine)
                problems.Add(new(line.VariantId, sku, "Quantity must be between 1 and 99."));
            if (!line.IsSavedForLater && variant.Product?.IsActive != true)
                problems.Add(new(line.VariantId, sku, "Product is inactive."));
            if (!line.IsSavedForLater && (variant.Inventory is null || variant.Inventory.QuantityAvailable < line.Quantity))
                problems.Add(new(line.VariantId, sku, "Insufficient available stock."));
            if (currencies.Length > 1) problems.Add(new(line.VariantId, sku, "All resulting cart lines must share one currency, including saved lines."));
        }
        return problems;
    }
}

public sealed class InvalidCartCombinationException(IReadOnlyList<CartLineProblem> problems)
    : DomainException("The proposed cart contains unusable lines.")
{
    public IReadOnlyList<CartLineProblem> Problems { get; } = problems;
}
