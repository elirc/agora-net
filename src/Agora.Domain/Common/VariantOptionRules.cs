namespace Agora.Domain.Common;

public static class VariantOptionRules
{
    public static Dictionary<string, string> Normalize(IReadOnlyDictionary<string, string> input)
    {
        if (input.Count > 20) throw new DomainException("At most 20 options are allowed.");
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in input)
        {
            var key = pair.Key.Trim();
            var value = pair.Value?.Trim();
            if (key.Length is < 1 or > 60 || string.IsNullOrEmpty(value) || value.Length > 120)
                throw new DomainException("Option keys must contain 1–60 characters and values 1–120 characters after trimming.");
            if (!normalized.TryAdd(key, value)) throw new DomainException("Option keys must be distinct after trimming, ignoring case.");
        }
        return normalized;
    }
}
