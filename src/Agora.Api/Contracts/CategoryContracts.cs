using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record CategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid? ParentCategoryId)
{
    public static CategoryResponse From(Category category) => new(
        category.Id,
        category.Name,
        category.Slug,
        category.Description,
        category.ParentCategoryId);
}

public sealed record CreateCategoryRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(200)] string? Slug,
    [MaxLength(2000)] string? Description,
    Guid? ParentCategoryId);

public sealed record UpdateCategoryRequest(
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(200)] string Slug,
    [MaxLength(2000)] string? Description,
    Guid? ParentCategoryId);
