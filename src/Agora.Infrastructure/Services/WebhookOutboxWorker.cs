using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agora.Infrastructure.Services;

public class WebhookOutboxRunner(AgoraDbContext db, TimeProvider clock, IServiceScopeFactory scopes)
{
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        DateTimeOffset now; var claimed = new List<(Guid Id, long Generation)>();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            now = clock.GetUtcNow();
            var expired = await db.WebhookDeliveries.Where(d => d.Status == WebhookDeliveryStatus.InFlight && d.LeaseExpiresAt <= now)
                .Include(d => d.Subscription).Take(10).ToListAsync(ct);
            foreach (var delivery in expired)
            {
                var attempt = await db.Set<WebhookAttempt>().SingleAsync(a => a.DeliveryId == delivery.Id && a.AttemptNumber == delivery.AttemptCount, ct);
                attempt.Finish(WebhookAttemptOutcome.Unknown, now, reasonCode: "LeaseExpired"); delivery.ExpireLease(now);
            }
            var due = await db.WebhookDeliveries.Include(d => d.Subscription)
                .Where(d => (d.Status == WebhookDeliveryStatus.Pending || d.Status == WebhookDeliveryStatus.Failed)
                    && d.AttemptCount < WebhookDelivery.MaxAttempts && d.DueAt <= now
                    && d.Subscription!.IsActive && !d.Subscription.IsDeleted)
                .OrderBy(d => d.DueAt).ThenBy(d => d.Id).Take(10).ToListAsync(ct);
            foreach (var delivery in due)
            {
                var attempt = delivery.Claim(now); db.Set<WebhookAttempt>().Add(attempt); claimed.Add((delivery.Id, delivery.LeaseGeneration));
            }
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
        await Task.WhenAll(claimed.Select(async claim =>
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<WebhookDeliverySender>().SendAsync(claim.Id, claim.Generation, ct);
        }));
        return claimed.Count;
    }
}

public class WebhookDeliverySender(AgoraDbContext db, IWebhookSender sender, TimeProvider clock)
{
    public async Task SendAsync(Guid id, long generation, CancellationToken stoppingToken)
    {
        WebhookDelivery delivery;
        await using (var transaction = await db.Database.BeginTransactionAsync(stoppingToken))
        {
            var started = clock.GetUtcNow();
            delivery = await db.WebhookDeliveries.SingleAsync(d => d.Id == id, stoppingToken);
            var attempt = await db.Set<WebhookAttempt>().SingleAsync(a => a.DeliveryId == id && a.LeaseGeneration == generation, stoppingToken);
            if (delivery.Status != WebhookDeliveryStatus.InFlight || delivery.LeaseGeneration != generation
                || delivery.LeaseExpiresAt <= started || attempt.Outcome != WebhookAttemptOutcome.Pending) return;
            if (!attempt.MarkSendInitiated(started)) return;
            await db.SaveChangesAsync(stoppingToken); await transaction.CommitAsync(stoppingToken);
            db.Entry(delivery).State = EntityState.Detached;
        }
        WebhookSendResult? result = null; string? uncertain = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            result = await sender.SendAsync(delivery.DestinationUrl, delivery.Payload, delivery.Signature, timeout.Token)
                .WaitAsync(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (TimeoutException) { uncertain = "Timeout"; }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { uncertain = "Timeout"; }
        catch (OperationCanceledException) { throw; }
        catch { uncertain = "TransportError"; }
        db.ChangeTracker.Clear();
        await using var completion = await db.Database.BeginTransactionAsync(stoppingToken);
        var finished = clock.GetUtcNow();
        var current = await db.WebhookDeliveries.SingleAsync(d => d.Id == id, stoppingToken);
        var currentAttempt = await db.Set<WebhookAttempt>().SingleAsync(a => a.DeliveryId == id && a.LeaseGeneration == generation, stoppingToken);
        if (currentAttempt.Outcome != WebhookAttemptOutcome.Pending) return;
        if (uncertain is not null)
        {
            if (current.Complete(generation, false, null, finished)) currentAttempt.Finish(WebhookAttemptOutcome.Unknown, finished, reasonCode: uncertain);
        }
        else if (current.Complete(generation, result!.Success, result.StatusCode, finished))
            currentAttempt.Finish(result.Success ? WebhookAttemptOutcome.Succeeded : WebhookAttemptOutcome.Failed,
                finished, result.StatusCode, result.Success ? null : "HttpRejected");
        await db.SaveChangesAsync(stoppingToken); await completion.CommitAsync(stoppingToken);
    }
}

public sealed class WebhookOutboxOptions { public bool Enabled { get; set; } = true; public int PollSeconds { get; set; } = 5; }

public sealed class WebhookOutboxWorker(IServiceScopeFactory scopes, IHostEnvironment environment,
    IOptions<WebhookOutboxOptions> options, ILogger<WebhookOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing") || !options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<WebhookOutboxRunner>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Webhook outbox iteration failed; durable work remains queued."); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollSeconds, 1, 300)), stoppingToken);
        }
    }
}
