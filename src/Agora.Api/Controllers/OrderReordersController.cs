using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Domain.Services;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize]
[Agora.Api.Filters.LocalSqliteWrite]
[Route("api/me/orders/{number}/reorder")]
public class OrderReordersController(OrderReorderService service, Agora.Api.Queries.CartResponseFactory responses) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CartResponse>> Create(string number, CancellationToken ct)
    {
        var owner = User.GetCustomerId();
        if (owner is null) return Unauthorized();
        try
        {
            var cart = await service.CreateAsync(owner.Value, number, ct);
            Response.Headers.CacheControl = "private, no-store";
            return Created($"/api/carts/{cart.Token}", await responses.CreateAsync(cart, ct));
        }
        catch (InvalidCartCombinationException error)
        {
            return UnprocessableEntity(new ProblemDetails { Title = error.Message, Extensions = { ["lines"] = error.Problems } });
        }
    }
}
