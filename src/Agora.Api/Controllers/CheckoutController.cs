using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/checkout")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("checkout")]
public class CheckoutController(CheckoutService checkoutService) : ControllerBase
{
    /// <summary>
    /// Converts a cart into a paid order. Stock is reserved before charging the
    /// payment gateway and committed (or released) based on the outcome.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Checkout(CheckoutRequest request, CancellationToken ct)
    {
        var order = await checkoutService.CheckoutAsync(
            new CheckoutInput(
                request.CartToken,
                request.Email,
                request.ShippingAddress?.ToAddress(),
                request.DiscountCode,
                request.PaymentToken,
                User.GetCustomerId(),
                request.ShippingMethodCode,
                request.ShippingAddressId,
                request.GiftCardCode),
            ct);

        return CreatedAtRoute("GetOrderByNumber", new { number = order.Number }, OrderResponse.From(order));
    }
}
