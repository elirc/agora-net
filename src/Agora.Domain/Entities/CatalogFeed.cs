using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum CatalogChangeKind { Upsert, Delete }

public sealed class CatalogChange
{
    public long Sequence { get; private set; }
    public Guid ProductId { get; private set; }
    public long ProductRevision { get; private set; }
    public CatalogChangeKind Kind { get; private set; }
    public int PayloadVersion { get; private set; } = 1;
    public string? PayloadJson { get; private set; }
    public int PayloadByteCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    private CatalogChange() { }
    public CatalogChange(Guid productId,long revision,CatalogChangeKind kind,string? payload,int bytes,DateTimeOffset now)
    {
        if(productId==Guid.Empty||revision<1||!Enum.IsDefined(kind)||bytes<0||bytes>262_144||
           (kind==CatalogChangeKind.Upsert&&(string.IsNullOrEmpty(payload)||bytes==0))||
           (kind==CatalogChangeKind.Delete&&(payload is not null||bytes!=0)))
            throw new DomainException("Catalog change metadata or payload is invalid.");
        ProductId=productId;ProductRevision=revision;Kind=kind;PayloadJson=payload;PayloadByteCount=bytes;CreatedAt=now;
    }
}

public sealed class CatalogFeedState
{
    public const int SingletonId=1;
    public int Id { get; private set; }=SingletonId;
    public long LastCommittedSequence { get; private set; }
    public long LastPurgedSequence { get; private set; }
    public long Version { get; private set; }
    private CatalogFeedState() { }
    public void Commit(long sequence){if(sequence<=LastCommittedSequence)throw new DomainException("Catalog sequence must advance.");var next=checked(Version+1);LastCommittedSequence=sequence;Version=next;}
    public void PurgeThrough(long sequence){if(sequence<LastPurgedSequence||sequence>LastCommittedSequence)throw new DomainException("Catalog retention floor is invalid.");var next=checked(Version+1);LastPurgedSequence=sequence;Version=next;}
}
public sealed class CatalogSnapshotTooLargeException(string message):DomainException(message);
public sealed class CatalogCursorException(string message,bool expired):DomainException(message){public bool Expired{get;}=expired;}
