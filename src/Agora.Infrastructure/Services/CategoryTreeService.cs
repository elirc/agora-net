using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record CategoryTreeNode(Guid Id, Guid? ParentCategoryId, string Name, string Slug, int? Depth);
public sealed record CategoryTreeSnapshot(long Version, bool IsValid, IReadOnlyList<CategoryTreeIssue> Issues, IReadOnlyList<CategoryTreeNode> Nodes);
public sealed record CategoryTreeMoveResult(Category Category, long Version);

public class CategoryTreeService(AgoraDbContext db)
{
    public async Task<CategoryTreeSnapshot> ReadAsync(CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = await db.Set<CategoryTreeState>().AsNoTracking().SingleAsync(s => s.Id == 1, ct);
        var categories = await LoadBounded(ct);
        var assessment = CategoryTreeRules.Assess(categories.ToDictionary(c => c.Id, c => c.ParentCategoryId));
        await transaction.CommitAsync(ct);
        return new(state.Version, assessment.IsValid, assessment.Issues, categories.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Id)
            .Select(c => new CategoryTreeNode(c.Id, c.ParentCategoryId, c.Name, c.Slug, assessment.Depths.GetValueOrDefault(c.Id))).ToArray());
    }

    public async Task<IReadOnlyList<CategoryTreeNode>> BreadcrumbsAsync(Guid id, CancellationToken ct = default)
    {
        var categories = await LoadBounded(ct); var nodes = categories.ToDictionary(c => c.Id);
        if (!nodes.ContainsKey(id)) throw new NotFoundException("Category not found.");
        var assessment = CategoryTreeRules.Assess(categories.ToDictionary(c => c.Id, c => c.ParentCategoryId));
        if (assessment.Depths.GetValueOrDefault(id) is not { } depth || depth > CategoryTreeRules.MaximumDepth)
            throw new InvalidCategoryTreeException(assessment.Issues);
        var result = new List<CategoryTreeNode>(); Guid? current = id;
        while (current is { } next)
        {
            var category = nodes[next];
            result.Add(new(category.Id, category.ParentCategoryId, category.Name, category.Slug, assessment.Depths[category.Id]));
            current = category.ParentCategoryId;
        }
        result.Reverse(); return result;
    }

    public async Task<CategoryTreeMoveResult> MoveAsync(Guid id, Guid? parentId, long expectedVersion, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = await db.Set<CategoryTreeState>().SingleAsync(s => s.Id == 1, ct);
        if (state.Version != expectedVersion) throw new CategoryTreeConflictException("The category tree changed. Reload its global revision.");
        var categories = await LoadBounded(ct); var map = categories.ToDictionary(c => c.Id, c => c.ParentCategoryId);
        if (!map.ContainsKey(id)) throw new NotFoundException("Category not found.");
        map[id] = parentId; CategoryTreeRules.EnsureValid(map);
        var category = await db.Categories.SingleAsync(c => c.Id == id, ct);
        if (category.ParentCategoryId != parentId)
        {
            category.ParentCategoryId = parentId; state.Advance(); await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct); return new(category, state.Version);
    }

    public async Task<Category> CreateAsync(string name, string? slugInput, string? description, Guid? parentId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = await db.Set<CategoryTreeState>().SingleAsync(s => s.Id == 1, ct);
        var categories = await LoadBounded(ct);
        var slug = string.IsNullOrWhiteSpace(slugInput) ? SlugGenerator.FromName(name) : slugInput.Trim();
        if (categories.Any(c => c.Slug == slug)) throw new CategoryTreeConflictException($"A category with slug '{slug}' already exists.");
        var category = new Category { Name = name.Trim(), Slug = slug, Description = description, ParentCategoryId = parentId };
        var map = categories.ToDictionary(c => c.Id, c => c.ParentCategoryId); map.Add(category.Id, parentId); CategoryTreeRules.EnsureValid(map);
        db.Categories.Add(category); state.Advance(); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return category;
    }

    public async Task<Category> UpdateAsync(Guid id, string name, string slugInput, string? description, Guid? parentId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = await db.Set<CategoryTreeState>().SingleAsync(s => s.Id == 1, ct);
        var categories = await LoadBounded(ct);
        if (categories.All(c => c.Id != id)) throw new NotFoundException("Category not found.");
        var slug = slugInput.Trim();
        if (categories.Any(c => c.Slug == slug && c.Id != id)) throw new CategoryTreeConflictException($"A category with slug '{slug}' already exists.");
        var map = categories.ToDictionary(c => c.Id, c => c.ParentCategoryId); map[id] = parentId; CategoryTreeRules.EnsureValid(map);
        var category = await db.Categories.SingleAsync(c => c.Id == id, ct);
        if (category.ParentCategoryId != parentId) state.Advance();
        category.Name = name.Trim(); category.Slug = slug; category.Description = description; category.ParentCategoryId = parentId;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return category;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = await db.Set<CategoryTreeState>().SingleAsync(s => s.Id == 1, ct);
        var categories = await LoadBounded(ct);
        if (categories.All(c => c.Id != id)) throw new NotFoundException("Category not found.");
        if (categories.Any(c => c.ParentCategoryId == id) || await db.Products.AnyAsync(p => p.CategoryId == id, ct))
            throw new CategoryTreeConflictException("Category has products or child categories and cannot be deleted.");
        var map = categories.Where(c => c.Id != id).ToDictionary(c => c.Id, c => c.ParentCategoryId); CategoryTreeRules.EnsureValid(map);
        db.Categories.Remove(await db.Categories.SingleAsync(c => c.Id == id, ct)); state.Advance();
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }

    private async Task<List<Category>> LoadBounded(CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.Id).Take(CategoryTreeRules.MaximumNodes + 1).ToListAsync(ct);
        if (categories.Count > CategoryTreeRules.MaximumNodes)
            throw new InvalidCategoryTreeException([new("CategoryLimitExceeded", null, null, "This implementation supports at most 5,000 categories; a larger-scale implementation is required.")]);
        return categories;
    }
}
