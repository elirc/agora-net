using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController, Authorize(Roles = "Admin"), Agora.Api.Filters.LocalSqliteWrite]
[Route("api/admin/orders/{number}/notes")]
public class OrderSupportNotesController(AgoraDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<OrderSupportNoteResponse>> Add(string number, AddOrderSupportNoteRequest request, CancellationToken ct)
    {
        var actor = User.GetCustomerId(); if (actor is null) return Unauthorized();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.AsNoTracking().Where(o => o.Number == number).Select(o => new { o.Id, o.Status }).SingleOrDefaultAsync(ct);
        if (order is null) return NotFound();
        if (order.Status == OrderStatus.Pending) return Conflict(new ProblemDetails { Title = "Support notes cannot be added to pending orders." });
        var note = new OrderSupportNote(order.Id, actor.Value, request.Body, clock.GetUtcNow());
        db.OrderSupportNotes.Add(note); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return CreatedAtAction(nameof(List), new { number }, OrderSupportNoteResponse.From(note));
    }
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderSupportNoteResponse>>> List(string number, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!QueryRules.ValidPage(page, pageSize)) return BadRequest(new ProblemDetails { Title = "Invalid pagination." });
        Response.Headers.CacheControl = "private, no-store";
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var id = await db.Orders.Where(o => o.Number == number).Select(o => (Guid?)o.Id).SingleOrDefaultAsync(ct);
        if (id is null) return NotFound();
        var query = db.OrderSupportNotes.AsNoTracking().Where(n => n.OrderId == id);
        var count = await query.CountAsync(ct);
        var notes = await query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new PagedResult<OrderSupportNoteResponse>(notes.Select(OrderSupportNoteResponse.From).ToArray(), page, pageSize, count));
    }
}
