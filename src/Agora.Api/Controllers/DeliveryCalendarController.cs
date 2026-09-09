using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/delivery-calendar")]
public class DeliveryCalendarController(AgoraDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DeliveryCalendarResponse>> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var calendar = await db.Set<DeliveryCalendar>().AsNoTracking().Include(c => c.Closures).SingleAsync(c => c.Id == 1, ct);
        return Ok(ToResponse(calendar));
    }
    [HttpPut, Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<DeliveryCalendarResponse>> Put(PutDeliveryCalendarRequest request, CancellationToken ct)
    {
        var parts = request.CutoffUtc.Split(':'); var minute = int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
        // Validate before loading a tracked entity so a rejected replacement has no partial state.
        try { _ = new DeliveryCalendar(request.Enabled, minute, request.ClosureDates); }
        catch (Agora.Domain.Common.DomainException error)
        {
            return UnprocessableEntity(new ProblemDetails { Title = error.Message });
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var calendar = await db.Set<DeliveryCalendar>().Include(c => c.Closures).SingleAsync(c => c.Id == 1, ct);
        if (calendar.Revision != request.ExpectedRevision) return Conflict(new ProblemDetails { Title = "Delivery calendar changed. Reload its revision." });
        calendar.Replace(request.Enabled, minute, request.ClosureDates);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); Response.Headers.CacheControl = "private, no-store";
        return Ok(ToResponse(calendar));
    }
    private static DeliveryCalendarResponse ToResponse(DeliveryCalendar calendar) => new(calendar.Enabled,
        $"{calendar.CutoffUtcMinute / 60:00}:{calendar.CutoffUtcMinute % 60:00}", calendar.Closures.Select(c => c.Date).Order().ToArray(), calendar.Revision);
}
