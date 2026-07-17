using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record CustomerAddressResponse(
    Guid Id,
    string Label,
    AddressDto Address,
    bool IsDefault,
    DateTimeOffset CreatedAt)
{
    public static CustomerAddressResponse From(CustomerAddress address) => new(
        address.Id,
        address.Label,
        AddressDto.From(address.Address),
        address.IsDefault,
        address.CreatedAt);
}

public sealed record SaveAddressRequest(
    [MaxLength(100)] string? Label,
    [Required] AddressDto Address,
    bool? IsDefault);
