using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record RegisterRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [MaxLength(200)] string? FullName);

public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MaxLength(128)] string Password);

public sealed record CustomerResponse(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    DateTimeOffset CreatedAt)
{
    public static CustomerResponse From(Customer customer) => new(
        customer.Id,
        customer.Email,
        customer.FullName,
        customer.Role.ToString(),
        customer.CreatedAt);
}

public sealed record AuthResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    CustomerResponse Customer);
