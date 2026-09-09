using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/report-exports")]
public sealed class ReportExportsController(
    AgoraDbContext db,
    ReportExportService service,
    TimeProvider clock) : ControllerBase
{
    [HttpPost]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<ReportExportResponse>> Create(
        CreateReportExportRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (request.Version != 1 || request.PaidFrom is null || request.PaidTo is null)
            return BadRequest(new ProblemDetails { Title = "Version 1 requires paidFrom and paidTo." });
        try
        {
            var job = await service.Queue(User.GetCustomerId()!.Value,
                request.PaidFrom.Value, request.PaidTo.Value, ct);
            return AcceptedAtAction(nameof(Get), new { id = job.Id }, ReportExportResponse.From(job));
        }
        catch (ReportExportCapacityException error)
        {
            return Conflict(new ProblemDetails { Title = error.Message, Status = 409 });
        }
        catch (Agora.Domain.Common.DomainException error)
        {
            return BadRequest(new ProblemDetails { Title = error.Message, Status = 400 });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReportExportResponse>> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var job = await service.Owned(id, User.GetCustomerId()!.Value, ct);
        return job is null ? NotFound() : Ok(ReportExportResponse.From(job));
    }

    [HttpPost("{id:guid}/cancel")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<ReportExportResponse>> Cancel(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var owner = User.GetCustomerId()!.Value;
        var job = await db.Set<ReportExportJob>()
            .SingleOrDefaultAsync(x => x.Id == id && x.RequesterId == owner, ct);
        if (job is null) return NotFound();
        job.Cancel(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(ReportExportResponse.From(job));
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var owner = User.GetCustomerId()!.Value;
        var job = await db.Set<ReportExportJob>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.RequesterId == owner, ct);
        if (job is null) return NotFound();
        if (job.ArtifactExpiresAt <= clock.GetUtcNow())
            return StatusCode(StatusCodes.Status410Gone);
        if (job.Status != ReportExportStatus.Succeeded)
            return Conflict(new ProblemDetails { Title = "The export is not ready for download.", Status = 409 });
        var artifact = await db.Set<ReportExportArtifact>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.JobId == id, ct);
        if (artifact is null) return StatusCode(StatusCodes.Status410Gone);
        return File(artifact.Content, "text/csv; charset=utf-8", $"sales-export-{id:N}.csv");
    }
}
