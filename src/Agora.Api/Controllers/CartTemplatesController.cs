using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize, Agora.Api.Filters.LocalSqliteWrite]
[Route("api/me/cart-templates")]
public class CartTemplatesController(AgoraDbContext db, CartTemplateService service, Agora.Api.Queries.CartResponseFactory responses) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CartTemplateResponse>> Create(CreateCartTemplateRequest request, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        try
        {
            var template = await service.CreateAsync(owner.Value, request.Name, request.CartToken, ct);
            return CreatedAtAction(nameof(Get), new { id = template.Id }, CartTemplateResponse.From(template));
        }
        catch (CartTemplateConflictException error) { return Conflict(new ProblemDetails { Title = error.Message }); }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CartTemplateSummary>>> List(CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        return Ok(await db.CartTemplates.AsNoTracking().Where(t => t.CustomerId == owner).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t => new CartTemplateSummary(t.Id, t.Name, t.CreatedAt, t.Lines.Count)).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CartTemplateResponse>> Get(Guid id, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        var template = await db.CartTemplates.AsNoTracking().Include(t => t.Lines).SingleOrDefaultAsync(t => t.Id == id && t.CustomerId == owner, ct);
        return template is null ? NotFound() : Ok(CartTemplateResponse.From(template));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        return await db.CartTemplates.Where(t => t.Id == id && t.CustomerId == owner).ExecuteDeleteAsync(ct) == 0 ? NotFound() : NoContent();
    }

    [HttpPost("{id:guid}/apply")]
    public async Task<ActionResult<CartResponse>> Apply(Guid id, ApplyCartTemplateRequest request, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        Response.Headers.CacheControl = "private, no-store";
        try { return Ok(await responses.CreateAsync(await service.ApplyAsync(owner.Value, id, request.TargetCartToken, request.ExpectedCartVersion!.Value, ct), ct)); }
        catch (CartTemplateConflictException error) { return Conflict(new ProblemDetails { Title = error.Message }); }
        catch (InvalidCartTemplateApplyException error)
        { return UnprocessableEntity(new ProblemDetails { Title = error.Message, Extensions = { ["lines"] = error.Problems } }); }
    }
}
