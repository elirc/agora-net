using Agora.Domain.Services;

namespace Agora.Tests.Unit;

public class CategoryTreeRulesTests
{
    [Fact]
    public void Iterative_walk_reports_cycles_missing_parents_and_depth_without_recursing()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var missing = Guid.NewGuid();
        var cycle = CategoryTreeRules.Assess(new Dictionary<Guid, Guid?> { [a] = b, [b] = c, [c] = a });
        Assert.False(cycle.IsValid); Assert.Contains(cycle.Issues, i => i.Code == "Cycle"); Assert.All(cycle.Depths.Values, Assert.Null);
        var orphan = CategoryTreeRules.Assess(new Dictionary<Guid, Guid?> { [a] = missing });
        Assert.Contains(orphan.Issues, i => i.Code == "MissingParent" && i.CategoryId == a && i.RelatedCategoryId == missing);
        var chain = new Dictionary<Guid, Guid?>(); Guid? parent = null;
        for (var i = 1; i <= 5000; i++) { var id = Guid.NewGuid(); chain.Add(id, parent); parent = id; }
        var deep = CategoryTreeRules.Assess(chain); Assert.Equal(5000, deep.Depths[parent!.Value]);
        Assert.Equal(4990, deep.Issues.Count(i => i.Code == "DepthExceeded"));
        chain.Add(Guid.NewGuid(), null); var oversized = CategoryTreeRules.Assess(chain);
        Assert.Contains(oversized.Issues, i => i.Code == "CategoryLimitExceeded"); Assert.Empty(oversized.Depths);
    }

    [Fact]
    public void Whole_tree_validation_includes_the_height_of_a_moved_subtree()
    {
        var root = Guid.NewGuid(); var child = Guid.NewGuid(); var leaf = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [root] = null, [child] = root, [leaf] = child };
        Guid? previous = null; Guid depthEight = default;
        for (var depth = 1; depth <= 9; depth++) { var id = Guid.NewGuid(); parents[id] = previous; previous = id; if (depth == 8) depthEight = id; }
        parents[child] = depthEight;
        var allowed = CategoryTreeRules.Assess(parents); Assert.True(allowed.IsValid); Assert.Equal(10, allowed.Depths[leaf]);
        parents[child] = previous;
        var rejected = CategoryTreeRules.Assess(parents); Assert.False(rejected.IsValid); Assert.Equal(10, rejected.Depths[child]); Assert.Equal(11, rejected.Depths[leaf]);
        Assert.Contains(rejected.Issues, i => i.CategoryId == leaf && i.Code == "DepthExceeded");
    }
}
