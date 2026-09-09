using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Agora.Api.Filters.LocalSqliteWrite]
[Route("api/admin/orders/{number}/guest-access")]
public sealed class GuestOrderCredentialsController(AgoraDbContext db, GuestOrderAccessService access) : ControllerBase
{
    [HttpPost("rotate")]
    public async Task<ActionResult<GuestCredentialResponse>> Rotate(string number, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Number == number, ct);
        if (order is null) return NotFound();
        var issue = await access.RotateAsync(order, User.GetCustomerId()!.Value, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(new GuestCredentialResponse(issue.Credential.Id, issue.Token, issue.Credential.ExpiresAt));
    }
}
