using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SavedSearchDefinition(string? Search = null, Guid? CategoryId = null, string? CategorySlug = null,
    decimal? MinPrice = null, decimal? MaxPrice = null, string? Currency = null, bool? InStock = null,
    bool? IsActive = null, string? Sort = null) : IValidatableObject
{
    public ProductSearchRequest ToRequest(int page = 1, int pageSize = 20) => new()
    {
        Search = Search, CategoryId = CategoryId, CategorySlug = CategorySlug, MinPrice = MinPrice,
        MaxPrice = MaxPrice, Currency = Currency, InStock = InStock, IsActive = IsActive,
        Sort = Sort, Page = page, PageSize = pageSize,
    };
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var request = ToRequest();
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), errors, validateAllProperties: true);
        return errors;
    }
}

public sealed record CreateSavedSearchRequest([Required] string Name, [Required] SavedSearchDefinition Definition);
public sealed record SavedSearchResponse(Guid Id, string Name, int SchemaVersion, DateTimeOffset CreatedAt,
    SavedSearchDefinition? Definition, bool CanRun, string? UnavailableReason);
public sealed record RecentProductResponse(DateTimeOffset LastViewedAt, ProductResponse Product);

internal static class SavedSearchPayload
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static (SavedSearchDefinition? Definition, string? Error) Interpret(SavedCatalogSearch saved)
    {
        if (saved.SchemaVersion != 1) return (null, $"Saved definition version {saved.SchemaVersion} is not supported and cannot be run.");
        try
        {
            var definition = JsonSerializer.Deserialize<SavedSearchDefinition>(saved.DefinitionJson, Options);
            if (definition is null || !Validator.TryValidateObject(definition, new ValidationContext(definition), [], true))
                return (null, "The saved definition no longer passes current catalog validation.");
            return (definition, null);
        }
        catch (JsonException) { return (null, "The saved definition cannot be interpreted safely."); }
    }
    public static SavedSearchResponse Response(SavedCatalogSearch saved)
    {
        var (definition, error) = Interpret(saved);
        return new(saved.Id, saved.Name, saved.SchemaVersion, saved.CreatedAt, definition, error is null, error);
    }
}
