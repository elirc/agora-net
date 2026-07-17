namespace Agora.Domain.Entities;

public enum CustomerRole
{
    Customer = 0,
    Admin = 1,
}

/// <summary>
/// Registered account. Carts and orders reference customers optionally so the
/// guest flow keeps working; <see cref="CustomerRole.Admin"/> unlocks the
/// catalog/inventory/discount mutation endpoints.
/// </summary>
public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public CustomerRole Role { get; set; } = CustomerRole.Customer;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
