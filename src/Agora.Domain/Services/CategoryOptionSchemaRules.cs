using Agora.Domain.Common;

namespace Agora.Domain.Services;

public sealed record CategoryOptionRule(string Key, bool Required, IReadOnlyList<string> AllowedValues);
public sealed record CategoryOptionViolation(string Key, string Reason, string? ActualValue);
public sealed record VariantOptionViolation(Guid? VariantId, string Sku, IReadOnlyList<CategoryOptionViolation> Violations);
public sealed class InvalidCategoryOptionsException(IReadOnlyList<VariantOptionViolation> violations)
    : DomainException("Variant options do not satisfy the enforced category schema.")
{
    public IReadOnlyList<VariantOptionViolation> Violations { get; } = violations;
}

public static class CategoryOptionSchemaRules
{
    public static IReadOnlyList<CategoryOptionRule> Normalize(IReadOnlyList<CategoryOptionRule> rules)
    {
        if (rules.Count > 10) throw new DomainException("A category schema allows at most ten option keys.");
        var result = new List<CategoryOptionRule>(); var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (rule is null) throw new DomainException("An option rule cannot be null.");
            var original = rule.Key?.Trim() ?? "";
            if (original.Length is < 1 or > 40 || original.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')))
                throw new DomainException("Schema keys require 1–40 ASCII letters, digits, underscores, or hyphens.");
            var key = original.ToLowerInvariant();
            if (!keys.Add(key)) throw new DomainException("Schema keys must be distinct after trimming and lowercasing.");
            if (rule.AllowedValues is null || rule.AllowedValues.Count is < 1 or > 50)
                throw new DomainException("Each option key needs 1–50 allowed values.");
            var values = rule.AllowedValues.Select(v => v?.Trim()).ToArray();
            if (values.Any(v => string.IsNullOrEmpty(v) || v.Length > 80) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new DomainException("Allowed values require 1–80 characters and ordinal uniqueness after trimming.");
            result.Add(new(key, rule.Required, values.Select(v => v!).Order(StringComparer.Ordinal).ToArray()));
        }
        return result.OrderBy(r => r.Key, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<CategoryOptionViolation> Validate(IReadOnlyList<CategoryOptionRule> rules, IReadOnlyDictionary<string, string> options)
    {
        var violations = new List<CategoryOptionViolation>();
        var known = rules.ToDictionary(r => r.Key, StringComparer.Ordinal); var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            var originalKey = option.Key.Trim();
            var key = originalKey.ToLowerInvariant(); var value = option.Value?.Trim();
            var safeKey = key.Length <= 40 ? key : key[..40]; var safeValue = value is null || value.Length <= 80 ? value : value[..80];
            if (originalKey.Any(c => c > 127))
            {
                violations.Add(new(originalKey.Length <= 40 ? originalKey : originalKey[..40], "InvalidKey", safeValue));
                continue;
            }
            if (!present.Add(key)) violations.Add(new(safeKey, "DuplicateKey", safeValue));
            if (!known.TryGetValue(key, out var rule)) violations.Add(new(safeKey, "UnknownKey", safeValue));
            else if (value is null || !rule.AllowedValues.Contains(value, StringComparer.Ordinal)) violations.Add(new(key, "ValueNotAllowed", safeValue));
        }
        foreach (var rule in rules.Where(r => r.Required && !present.Contains(r.Key))) violations.Add(new(rule.Key, "RequiredKeyMissing", null));
        return violations.OrderBy(v => v.Key, StringComparer.Ordinal).ThenBy(v => v.Reason, StringComparer.Ordinal).ToArray();
    }

    public static bool SameOptions(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second) =>
        first.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(second.OrderBy(p => p.Key, StringComparer.Ordinal));
}
