using System.Text;

namespace Agora.Domain.Common;

public static class SlugGenerator
{
    /// <summary>Lowercases, strips non-alphanumerics, and collapses runs into single hyphens.</summary>
    public static string FromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Cannot generate a slug from an empty name.");
        }

        var builder = new StringBuilder(name.Length);
        var lastWasHyphen = true; // suppress leading hyphen

        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = builder.ToString().TrimEnd('-');
        if (slug.Length == 0)
        {
            throw new DomainException($"Cannot generate a slug from '{name}'.");
        }

        return slug;
    }
}
