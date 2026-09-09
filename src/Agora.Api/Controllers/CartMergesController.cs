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
[Route("api/me/carts/merge")]
public class CartMergesController(CartMergeService service, Agora.Api.Queries.CartResponseFactory responses) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CartMergeResponse>> Merge(MergeCartsRequest request, CancellationToken ct)
    {
        var owner = User.GetCustomerId();
        if (owner is null) return Unauthorized();
        try
        {
            var result = await service.MergeAsync(owner.Value, request.SourceToken, request.TargetToken,
                request.ExpectedSourceVersion!.Value, request.ExpectedTargetVersion!.Value, ct);
            Response.Headers.CacheControl = "private, no-store";
            return Ok(new CartMergeResponse(await responses.CreateAsync(result.Target, ct), result.SourceVersion, result.Target.Version));
        }
        catch (CartMergeConflictException error) { return Conflict(new ProblemDetails { Title = error.Message }); }
        catch (InvalidCartCombinationException error)
        { return UnprocessableEntity(new ProblemDetails { Title = error.Message, Extensions = { ["lines"] = error.Problems } }); }
    }
}
