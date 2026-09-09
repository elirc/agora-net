using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Agora.Domain.Services;

namespace Agora.Api.Contracts;

public sealed record CategoryOptionRuleRequest([Required, MaxLength(40)] string Key, bool Required,
    [Required, MinLength(1), MaxLength(50)] List<string> AllowedValues);
public sealed record PutCategoryOptionSchemaRequest([Required, MaxLength(16)] string Mode,
    [Required, MaxLength(10)] List<CategoryOptionRuleRequest> Rules,
    [property: JsonRequired] [Range(0, long.MaxValue)] long? ExpectedRevision);
public sealed record CategoryOptionSchemaResponse(Guid CategoryId, string Mode, int SchemaVersion, long? Revision,
    IReadOnlyList<CategoryOptionRule> Rules);
public sealed record CategoryOptionViolationResponse(Guid VariantId, string Sku, IReadOnlyList<CategoryOptionViolation> Violations);
