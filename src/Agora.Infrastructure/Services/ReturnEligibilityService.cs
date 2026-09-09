using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Agora.Infrastructure.Services;

public sealed class ReturnPolicyOptions
{
    public const string SectionName = "ReturnPolicy";
    [Range(1, 365)] public int? WindowDays { get; set; }
}

public sealed record ReturnEligibilityLine(Guid OrderItemId, string Sku, int PurchasedQuantity, int RemainingQuantity, decimal EstimatedRefund);
public sealed record ReturnEligibilityResult(DateTimeOffset EvaluatedAt, DateTimeOffset? Deadline, bool Eligible,
    IReadOnlyList<string> Reasons, string Currency, IReadOnlyList<ReturnEligibilityLine> Lines);

public class ReturnEligibilityService(AgoraDbContext db, IOptions<ReturnPolicyOptions> options)
{
    public async Task<ReturnEligibilityResult> EvaluateAsync(Order order, DateTimeOffset now, CancellationToken ct = default)
    {
        var policy = ReturnEligibilityRules.Evaluate(order.Status, order.FulfilledAt, options.Value.WindowDays, now);
        var used = await db.ReturnRequestItems.AsNoTracking().Where(i => i.ReturnRequest!.OrderId == order.Id
            && (i.ReturnRequest.Status == ReturnStatus.Requested || i.ReturnRequest.Status == ReturnStatus.Approved))
            .GroupBy(i => i.OrderItemId).Select(g => new { Id = g.Key, Quantity = g.Sum(i => (long)i.Quantity) })
            .ToDictionaryAsync(g => g.Id, g => g.Quantity, ct);
        var lines = order.Items.OrderBy(i => i.Sku, StringComparer.Ordinal).ThenBy(i => i.Id).Select(item =>
        {
            var remaining = checked((int)Math.Clamp((long)item.Quantity - used.GetValueOrDefault(item.Id), 0L, item.Quantity));
            return new ReturnEligibilityLine(item.Id, item.Sku, item.Quantity, remaining, ReturnEligibilityRules.EstimateRefund(order, item, remaining));
        }).ToArray();
        var reasons = policy.Reasons.ToList();
        if (!lines.Any(l => l.RemainingQuantity > 0)) reasons.Add("NoRemainingQuantity");
        return new(now, policy.Deadline, reasons.Count == 0, reasons, order.Currency, lines);
    }
}
