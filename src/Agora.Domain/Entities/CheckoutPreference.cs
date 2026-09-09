using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public class CheckoutPreference
{
    public Guid CustomerId { get; private set; }
    public Guid? ShippingAddressId { get; private set; }
    public string? ShippingMethodCode { get; private set; }
    public long Version { get; private set; }
    private CheckoutPreference() { }
    public CheckoutPreference(Guid owner, Guid? addressId, string? methodCode)
    {
        if (owner == Guid.Empty) throw new DomainException("An owner is required.");
        CustomerId = owner; ShippingAddressId = addressId; ShippingMethodCode = methodCode;
    }
    public void Replace(Guid? addressId, string? methodCode)
    {
        var next = checked(Version + 1);
        ShippingAddressId = addressId; ShippingMethodCode = methodCode; Version = next;
    }
}
