namespace Agora.Domain.Common;

/// <summary>Shared authoring rules for immutable tag and collection slugs.</summary>
public static class CatalogText
{
    public const string SlugPattern = "^[a-z0-9]+(?:-[a-z0-9]+)*$";

    public static string Slug(string? value)
    {
        var slug = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (slug.Length is < 1 or > 60 || slug.StartsWith('-') || slug.EndsWith('-') || slug.Contains("--")
            || slug.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-'))
            throw new DomainException("Slug must contain 1–60 ASCII letters or digits separated by single hyphens.");
        return slug;
    }

    public static string Name(string? value, int maximum)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length < 1 || name.Length > maximum)
            throw new DomainException($"Name must contain 1–{maximum} characters after trimming.");
        return name;
    }
}
