using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum ReportExportStatus { Queued, Running, Succeeded, Failed, Cancelled }

public sealed class ReportExportJob
{
    private ReportExportJob() { }
    public ReportExportJob(Guid owner, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        if (owner == Guid.Empty || to <= from || to - from > TimeSpan.FromDays(90))
            throw new DomainException("Export range must be increasing and at most 90 days.");
        Id = Guid.NewGuid(); RequesterId = owner; PaidFrom = from; PaidTo = to; CreatedAt = now;
    }
    public Guid Id { get; private set; }
    public Guid RequesterId { get; private set; }
    public DateTimeOffset PaidFrom { get; private set; }
    public DateTimeOffset PaidTo { get; private set; }
    public int QueryVersion { get; private set; } = 1;
    public ReportExportStatus Status { get; private set; }
    public long LeaseGeneration { get; private set; }
    public int ClaimCount { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public bool CancellationRequested { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SourceSnapshotAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ArtifactExpiresAt { get; private set; }
    public string? FailureCode { get; private set; }

    public long Claim(DateTimeOffset now)
    {
        if (CancellationRequested) { Status = ReportExportStatus.Cancelled; CompletedAt = now; return LeaseGeneration; }
        if (Status != ReportExportStatus.Queued
            && !(Status == ReportExportStatus.Running && LeaseExpiresAt <= now))
            throw new DomainException("Job cannot be claimed.");
        if (ClaimCount >= 3) { Fail("ClaimsExhausted", now); return LeaseGeneration; }
        Status = ReportExportStatus.Running; ClaimCount++; LeaseGeneration = checked(LeaseGeneration + 1);
        LeaseExpiresAt = now.AddMinutes(2); return LeaseGeneration;
    }
    public void Cancel(DateTimeOffset now)
    {
        if (CancellationRequested || Status is ReportExportStatus.Succeeded or ReportExportStatus.Failed or ReportExportStatus.Cancelled) return;
        CancellationRequested = true; LeaseGeneration = checked(LeaseGeneration + 1);
        if (Status == ReportExportStatus.Queued) { Status = ReportExportStatus.Cancelled; CompletedAt = now; }
    }
    public bool Publish(long generation, DateTimeOffset snapshot, DateTimeOffset now)
    {
        if (Status != ReportExportStatus.Running || LeaseGeneration != generation || LeaseExpiresAt <= now || CancellationRequested) return false;
        Status = ReportExportStatus.Succeeded; SourceSnapshotAt = snapshot; CompletedAt = now;
        ArtifactExpiresAt = now.AddHours(24); LeaseExpiresAt = null; LeaseGeneration = checked(LeaseGeneration + 1); return true;
    }
    public void Fail(string code, DateTimeOffset now)
    {
        Status = CancellationRequested ? ReportExportStatus.Cancelled : ReportExportStatus.Failed;
        FailureCode = code; CompletedAt = now; LeaseExpiresAt = null; LeaseGeneration = checked(LeaseGeneration + 1);
    }
}

public sealed class ReportExportArtifact
{
    private ReportExportArtifact() { }
    public ReportExportArtifact(Guid id, byte[] content, string digest)
    { JobId = id; Content = content; Digest = digest; }
    public Guid JobId { get; private set; }
    public byte[] Content { get; private set; } = [];
    public string Digest { get; private set; } = "";
}
