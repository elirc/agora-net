using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/checkout")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("checkout")]
public class CheckoutController(CheckoutService checkoutService, CheckoutPricingService pricingService) : ControllerBase
{
    [HttpPost("quote")]
    public async Task<ActionResult<CheckoutQuoteResponse>> Quote(CheckoutQuoteRequest request, CancellationToken ct)
    {
        if (request.UseSavedPreferences && User.GetCustomerId() is null) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        var result = await pricingService.CalculateAsync(new CheckoutPricingInput(request.CartToken,
            request.ShippingAddress?.ToAddress(), request.DiscountCode, User.GetCustomerId(), request.ShippingMethodCode,
            request.ShippingAddressId, request.GiftCardCode, request.UseSavedPreferences), tracking: false, ct);
        return Ok(CheckoutQuoteResponse.From(result));
    }

    /// <summary>
    /// Converts a cart into a paid order. Stock is reserved before charging the
    /// payment gateway and committed (or released) based on the outcome.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CheckoutResponse>> Checkout(CheckoutRequest request, CancellationToken ct)
    {
        if (request.UseSavedPreferences && User.GetCustomerId() is null) return Unauthorized();
        var result = await checkoutService.CheckoutAsync(
            new CheckoutInput(
                request.CartToken,
                request.Email,
                request.ShippingAddress?.ToAddress(),
                request.DiscountCode,
                request.PaymentToken,
                User.GetCustomerId(),
                request.ShippingMethodCode,
                request.ShippingAddressId,
                request.GiftCardCode,
                request.UseSavedPreferences),
            ct);

        Response.Headers.CacheControl = "private, no-store";
        return CreatedAtRoute("GetOrderByNumber", new { number = result.Order.Number }, CheckoutResponse.From(result));
    }
}
