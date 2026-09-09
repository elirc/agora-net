using System.Text.Json;
using Agora.Domain.Common;
using Agora.Domain.Services;

namespace Agora.Domain.Entities;

public enum CategoryOptionSchemaMode { Off = 0, Observe = 1, Enforce = 2 }

public class CategoryOptionSchema
{
    public Guid CategoryId { get; private set; }
    public CategoryOptionSchemaMode Mode { get; private set; }
    public int SchemaVersion { get; private set; } = 1;
    public string RulesJson { get; private set; } = "[]";
    public long Revision { get; private set; }
    private CategoryOptionSchema() { }
    public CategoryOptionSchema(Guid categoryId, CategoryOptionSchemaMode mode, IReadOnlyList<CategoryOptionRule> rules)
    {
        if (!Enum.IsDefined(mode)) throw new DomainException("Unknown option schema mode.");
        var normalized = CategoryOptionSchemaRules.Normalize(rules);
        CategoryId = categoryId; Mode = mode; RulesJson = JsonSerializer.Serialize(normalized);
    }
    public void Replace(CategoryOptionSchemaMode mode, IReadOnlyList<CategoryOptionRule> rules)
    {
        if (!Enum.IsDefined(mode)) throw new DomainException("Unknown option schema mode.");
        var normalized = CategoryOptionSchemaRules.Normalize(rules); var next = checked(Revision + 1);
        Mode = mode; SchemaVersion = 1; RulesJson = JsonSerializer.Serialize(normalized); Revision = next;
    }
    public IReadOnlyList<CategoryOptionRule> ReadRules()
    {
        if (SchemaVersion != 1) throw new CategoryOptionSchemaStateException("This option schema version is not supported.");
        try { return CategoryOptionSchemaRules.Normalize(JsonSerializer.Deserialize<CategoryOptionRule[]>(RulesJson) ?? throw new JsonException()); }
        catch (Exception error) when (error is JsonException or DomainException or ArgumentException)
        { throw new CategoryOptionSchemaStateException("The stored option schema is invalid and requires an explicit repair."); }
    }
}

public sealed class CategoryOptionSchemaStateException(string message) : DomainException(message);
