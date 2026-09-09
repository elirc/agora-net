namespace Agora.Api.Contracts;

internal static class ProductInputRules
{
    public const int NameLength = 200;
    public const int SlugLength = 200;
    public const int SkuLength = 64;
    public static List<string> NormalizeSkus(IEnumerable<string> skus) => skus.Select(s => s.Trim()).ToList();
    public static bool HasDuplicateSkus(IReadOnlyCollection<string> skus) => skus.Distinct(StringComparer.OrdinalIgnoreCase).Count() != skus.Count;
}
