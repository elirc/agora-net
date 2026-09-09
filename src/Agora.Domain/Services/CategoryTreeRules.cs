using Agora.Domain.Common;

namespace Agora.Domain.Services;

public sealed record CategoryTreeIssue(string Code, Guid? CategoryId, Guid? RelatedCategoryId, string Message);
public sealed record CategoryTreeAssessment(IReadOnlyDictionary<Guid, int?> Depths, IReadOnlyList<CategoryTreeIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
public sealed class InvalidCategoryTreeException(IReadOnlyList<CategoryTreeIssue> issues)
    : DomainException("The category tree does not satisfy the bounded topology rules.")
{
    public IReadOnlyList<CategoryTreeIssue> Issues { get; } = issues;
}
public sealed class CategoryTreeConflictException(string message) : DomainException(message);

public static class CategoryTreeRules
{
    public const int MaximumNodes = 5000;
    public const int MaximumDepth = 10;

    /// <summary>Iterative, memoized parent walks. Each node is resolved once; cycles never recurse.</summary>
    public static CategoryTreeAssessment Assess(IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var depths = new Dictionary<Guid, int?>(); var issues = new List<CategoryTreeIssue>();
        if (parents.Count > MaximumNodes)
            return new(depths, [new("CategoryLimitExceeded", null, null, "This implementation supports at most 5,000 categories; use a larger-scale implementation for this tree.")]);
        foreach (var start in parents.Keys.Order())
        {
            if (depths.ContainsKey(start)) continue;
            var path = new List<Guid>(); var seen = new HashSet<Guid>(); var current = start; int? baseDepth;
            while (true)
            {
                if (depths.TryGetValue(current, out baseDepth)) break;
                if (!parents.TryGetValue(current, out var parent))
                {
                    issues.Add(new("MissingParent", path.LastOrDefault(), current, "A referenced parent is absent."));
                    baseDepth = null; break;
                }
                if (!seen.Add(current))
                {
                    issues.Add(new("Cycle", current, parents[current], "A parent path revisits a category."));
                    baseDepth = null; break;
                }
                path.Add(current);
                if (parent is null) { baseDepth = 0; break; }
                current = parent.Value;
            }
            for (var i = path.Count - 1; i >= 0; i--)
            {
                if (baseDepth is not null) baseDepth++;
                depths[path[i]] = baseDepth;
                if (baseDepth > MaximumDepth)
                    issues.Add(new("DepthExceeded", path[i], parents[path[i]], "Root depth is one; maximum resulting depth is ten."));
            }
        }
        return new(depths, issues.OrderBy(i => i.CategoryId).ThenBy(i => i.Code, StringComparer.Ordinal).ToArray());
    }

    public static void EnsureValid(IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var assessment = Assess(parents);
        if (!assessment.IsValid) throw new InvalidCategoryTreeException(assessment.Issues);
    }
}
