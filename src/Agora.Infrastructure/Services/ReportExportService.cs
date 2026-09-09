using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agora.Infrastructure.Services;

public sealed class ReportExportCapacityException(string message) : DomainException(message);

public sealed class ReportExportService(AgoraDbContext db, TimeProvider clock)
{
    public async Task<ReportExportJob> Queue(Guid owner, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var active = await db.Set<ReportExportJob>().Where(x => x.RequesterId == owner &&
            (x.Status == ReportExportStatus.Queued || x.Status == ReportExportStatus.Running)).Take(11).CountAsync(ct);
        if (active >= 10) throw new ReportExportCapacityException("At most ten active report exports are allowed per administrator.");
        var job = new ReportExportJob(owner, from, to, clock.GetUtcNow());
        db.Add(job); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return job;
    }
    public Task<ReportExportJob?> Owned(Guid id, Guid owner, CancellationToken ct) => db.Set<ReportExportJob>()
        .AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.RequesterId == owner, ct);
}

public sealed class ReportExportRunner(IServiceScopeFactory scopes, TimeProvider clock)
{
    public const int MaximumOrders = 10_000;
    public const int MaximumBytes = 10 * 1024 * 1024;

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var cleaned = await CleanupAsync(ct);
        var claim = await ClaimAsync(ct);
        if (claim is null) return cleaned;
        var (jobId, generation, from, to) = claim.Value;
        ExportBuild build;
        try { build = await BuildAsync(from, to, ct); }
        catch { await FinishFailureAsync(jobId, generation, "BuildFailed", ct); throw; }
        await PublishAsync(jobId, generation, build, ct);
        return 1;
    }

    private async Task<(Guid Id,long Generation,DateTimeOffset From,DateTimeOffset To)?> ClaimAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct); var now = clock.GetUtcNow();
        var job = await db.Set<ReportExportJob>().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(x => x.Status == ReportExportStatus.Queued ||
                (x.Status == ReportExportStatus.Running && x.LeaseExpiresAt <= now), ct);
        if (job is null) return null;
        var generation = job.Claim(now);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { return null; }
        return job.Status == ReportExportStatus.Running ? (job.Id,generation,job.PaidFrom,job.PaidTo) : null;
    }

    private async Task<ExportBuild> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var snapshot = clock.GetUtcNow();
        var rows = await db.Orders.AsNoTracking().Where(o => o.PaidAt >= from && o.PaidAt < to)
            .OrderBy(o => o.PaidAt).ThenBy(o => o.Id).Take(MaximumOrders + 1)
            .Select(o => new ExportRow(o.Number,o.PaidAt!.Value,o.Status.ToString(),o.Currency,
                o.Items.Sum(i => i.Quantity),o.Subtotal,o.DiscountAmount,o.TaxAmount,o.ShippingAmount,o.Total)).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        if (rows.Length > MaximumOrders) return new(snapshot,null,"OrderLimitExceeded");
        using var stream = new LimitedStream(MaximumBytes);
        var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { NewLine = "\r\n" };
        try
        {
            await writer.WriteLineAsync("orderNumber,paidAt,status,currency,purchasedQuantity,subtotal,discount,tax,shipping,total");
            foreach (var r in rows) await writer.WriteLineAsync(string.Join(',',Cell(r.Number),r.PaidAt.ToString("O",CultureInfo.InvariantCulture),
                Cell(r.Status),Cell(r.Currency),r.Quantity.ToString(CultureInfo.InvariantCulture),Amount(r.Subtotal),Amount(r.Discount),
                Amount(r.Tax),Amount(r.Shipping),Amount(r.Total)));
            await writer.FlushAsync(ct);
            await writer.DisposeAsync();
            return new(snapshot,stream.ToArray(),null);
        }
        catch (ExportLimitException)
        {
            // Do not dispose/flush an already-over-limit writer: there is no
            // artifact to preserve and another flush could throw outside this guard.
            return new(snapshot,null,"ByteLimitExceeded");
        }
    }

    private async Task PublishAsync(Guid id,long generation,ExportBuild build,CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var job = await db.Set<ReportExportJob>().SingleAsync(x => x.Id == id, ct);
        var now = clock.GetUtcNow();
        if (job.Status != ReportExportStatus.Running || job.LeaseGeneration != generation
            || job.LeaseExpiresAt <= now) return;
        if (build.Failure is not null) job.Fail(build.Failure, now);
        else if (job.Publish(generation, build.SnapshotAt, now))
            db.Add(new ReportExportArtifact(id, build.Bytes!, Convert.ToHexString(SHA256.HashData(build.Bytes!))));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    private Task FinishFailureAsync(Guid id,long generation,string code,CancellationToken ct)=>PublishAsync(id,generation,new(clock.GetUtcNow(),null,code),ct);

    public async Task<int> CleanupAsync(CancellationToken ct=default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();
        var now = clock.GetUtcNow();
        var ids = await db.Set<ReportExportArtifact>()
            .Join(db.Set<ReportExportJob>().Where(x => x.ArtifactExpiresAt <= now),
                artifact => artifact.JobId, job => job.Id,
                (artifact, job) => new { job.Id, job.ArtifactExpiresAt })
            .OrderBy(x => x.ArtifactExpiresAt).ThenBy(x => x.Id)
            .Select(x => x.Id).Take(25).ToArrayAsync(ct);
        var artifacts = await db.Set<ReportExportArtifact>()
            .Where(x => ids.Contains(x.JobId)).ToArrayAsync(ct);
        db.RemoveRange(artifacts);
        await db.SaveChangesAsync(ct);
        return artifacts.Length;
    }
    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Cell(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }
    private sealed record ExportRow(string Number,DateTimeOffset PaidAt,string Status,string Currency,int Quantity,decimal Subtotal,decimal Discount,decimal Tax,decimal Shipping,decimal Total);
    private sealed record ExportBuild(DateTimeOffset SnapshotAt,byte[]? Bytes,string? Failure);
    private sealed class ExportLimitException:Exception { }
    private sealed class LimitedStream(int maximum) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        { Check(count); return base.WriteAsync(buffer, offset, count, ct); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { Check(buffer.Length); return base.WriteAsync(buffer, ct); }
        private void Check(int count) { if (Length + count > maximum) throw new ExportLimitException(); }
    }
}

public sealed class ReportExportOptions
{
    public const string SectionName = "ReportExports";
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 10;
}

public sealed class ReportExportWorker(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    IOptions<ReportExportOptions> options,
    ILogger<ReportExportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if (environment.IsEnvironment("Testing") || !options.Value.Enabled) return;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReportExportRunner>().RunOnceAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogError(error, "Report export iteration failed; durable job remains claimable.");
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollSeconds, 1, 300)), token);
        }
    }
}
