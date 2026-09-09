using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Services;

namespace Agora.Api.Queries;

public sealed class CartResponseFactory(VariantLinePricingService pricing)
{
    public async Task<CartResponse> CreateAsync(Cart cart, CancellationToken ct = default) =>
        CartResponse.From(cart, await pricing.CalculateAsync(cart.Items, ct));
}
