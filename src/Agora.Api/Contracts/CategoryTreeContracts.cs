using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Agora.Api.Contracts;

public sealed record MoveCategoryRequest([property: JsonRequired] Guid? NewParentCategoryId,
    [Required, Range(0, long.MaxValue)] long? ExpectedTreeVersion);
public sealed record CategoryMoveResponse(CategoryResponse Category, long TreeVersion);
