using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize, Agora.Api.Filters.LocalSqliteWrite]
[Route("api")]
public class ReturnEvidenceController(AgoraDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet("me/returns/{number}/evidence")]
    public async Task<ActionResult<IReadOnlyList<ReturnEvidenceResponse>>> Mine(string number, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        var id = await OwnedId(number, owner.Value, ct); return id is null ? NotFound() : Ok(await Read(id.Value, ct));
    }
    [Authorize(Roles = "Admin")]
    [HttpGet("admin/returns/{number}/evidence")]
    public async Task<ActionResult<IReadOnlyList<ReturnEvidenceResponse>>> Admin(string number, CancellationToken ct)
    {
        var id = await db.ReturnRequests.Where(r => r.Number == number).Select(r => (Guid?)r.Id).SingleOrDefaultAsync(ct);
        return id is null ? NotFound() : Ok(await Read(id.Value, ct));
    }
    [HttpPost("me/returns/{number}/evidence")]
    public async Task<ActionResult<ReturnEvidenceResponse>> Add(string number, AddReturnEvidenceRequest request, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var id = await OwnedId(number, owner.Value, ct); if (id is null) return NotFound();
        if (await db.ReturnEvidence.CountAsync(e => e.ReturnRequestId == id, ct) >= 5)
            return Conflict(new ProblemDetails { Title = "A return can contain at most five evidence links." });
        var evidence = new ReturnEvidence(id.Value, owner.Value, request.Url, request.Description, clock.GetUtcNow());
        db.ReturnEvidence.Add(evidence); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return CreatedAtAction(nameof(Mine), new { number }, ReturnEvidenceResponse.From(evidence));
    }
    [HttpDelete("me/returns/{number}/evidence/{id:guid}")]
    public async Task<IActionResult> Delete(string number, Guid id, CancellationToken ct)
    {
        var owner = User.GetCustomerId(); if (owner is null) return Unauthorized();
        var returnId = await OwnedId(number, owner.Value, ct); if (returnId is null) return NotFound();
        return await db.ReturnEvidence.Where(e => e.Id == id && e.ReturnRequestId == returnId).ExecuteDeleteAsync(ct) == 0 ? NotFound() : NoContent();
    }
    private Task<Guid?> OwnedId(string number, Guid owner, CancellationToken ct) => db.ReturnRequests
        .Where(r => r.Number == number && r.Order!.CustomerId == owner).Select(r => (Guid?)r.Id).SingleOrDefaultAsync(ct);
    private async Task<ReturnEvidenceResponse[]> Read(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        return (await db.ReturnEvidence.AsNoTracking().Where(e => e.ReturnRequestId == id).OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(ct)).Select(ReturnEvidenceResponse.From).ToArray();
    }
}
