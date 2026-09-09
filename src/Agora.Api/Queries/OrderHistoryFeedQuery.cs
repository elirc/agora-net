using Agora.Api.Contracts;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Queries;

public sealed record OrderHistoryFeedResponse(IReadOnlyList<CustomerOrderResponse> Items, bool HasMore, string? NextCursor);

public sealed class OrderHistoryFeedQuery(AgoraDbContext db, OrderHistoryCursorProtector protector, TimeProvider clock)
{
    public async Task<OrderHistoryFeedResponse> ReadAsync(Guid owner, int limit, string? encoded, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw new InvalidOrderHistoryCursorException();
        var now = clock.GetUtcNow();
        var cursor = encoded is null ? null : protector.Read(encoded, owner, limit, now);
        var cutoff = cursor is null ? now : new DateTimeOffset(cursor.CutoffTicks, TimeSpan.Zero);
        var expiry = cursor?.ExpiresTicks ?? now.AddHours(24).UtcTicks;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var query = db.Orders.AsNoTracking().Where(o => o.CustomerId == owner && o.CreatedAt <= cutoff);
        if (cursor is not null)
        {
            var lastTime = new DateTimeOffset(cursor.LastCreatedTicks, TimeSpan.Zero);
            var lastNumber = cursor.LastNumber;
            query = query.Where(o => o.CreatedAt < lastTime || o.CreatedAt == lastTime
                && string.Compare(EF.Functions.Collate(o.Number, "BINARY"), lastNumber) < 0);
        }
        // Keyset seek: no Skip and no total Count. One extra key establishes HasMore.
        var keys = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => EF.Functions.Collate(o.Number, "BINARY"))
            .Take(limit + 1).Select(o => new { o.Id, o.CreatedAt, o.Number }).ToArrayAsync(ct);
        var hasMore = keys.Length > limit;
        var page = keys.Take(limit).ToArray();
        var ids = page.Select(k => k.Id).ToArray();
        var rows = ids.Length == 0 ? [] : await db.Orders.AsNoTracking().Where(o => o.CustomerId == owner && ids.Contains(o.Id))
            .Include(o => o.Items).AsSplitQuery().ToArrayAsync(ct);
        var byId = rows.ToDictionary(o => o.Id);
        var items = page.Select(k => CustomerOrderResponse.From(byId[k.Id])).ToArray();
        string? next = null;
        if (hasMore)
        {
            var last = page[^1];
            next = protector.Protect(new OrderHistoryCursor(1, owner, cutoff.UtcTicks, last.CreatedAt.UtcTicks, last.Number, limit, expiry));
        }
        await transaction.CommitAsync(ct);
        return new(items, hasMore, next);
    }
}
