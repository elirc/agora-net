namespace Agora.Domain.Entities;

/// <summary>A saved entry in a customer's address book.</summary>
public class CustomerAddress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }

    /// <summary>Friendly label, e.g. "Home" or "Office".</summary>
    public string Label { get; set; } = string.Empty;

    public Address Address { get; set; } = new();
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
