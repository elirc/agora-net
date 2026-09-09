using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum CatalogImportState { DraftValid, DraftInvalid, Applied }

public sealed class CatalogImport
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public int ProposalVersion { get; private set; } = 1;
    public string ProposalJson { get; private set; } = "";
    public string Digest { get; private set; } = "";
    public string ErrorsJson { get; private set; } = "[]";
    public Guid AuthorId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public long Revision { get; private set; }
    public CatalogImportState State { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }
    public List<CatalogImportResult> Results { get; private set; } = [];
    private CatalogImport() { }
    public CatalogImport(string proposalJson, string digest, string errorsJson, bool valid, Guid author, DateTimeOffset now)
    {
        ProposalJson = proposalJson; Digest = digest; ErrorsJson = errorsJson;
        AuthorId = author; CreatedAt = now; ExpiresAt = now.AddHours(24);
        State = valid ? CatalogImportState.DraftValid : CatalogImportState.DraftInvalid;
    }
    public void Apply(IEnumerable<CatalogImportResult> results, DateTimeOffset now)
    {
        if (State != CatalogImportState.DraftValid) throw new DomainException("Only a valid draft can be applied.");
        Revision = checked(Revision + 1); Results.AddRange(results); State = CatalogImportState.Applied; AppliedAt = now;
    }
}

public sealed class CatalogImportResult
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CatalogImportId { get; private set; }
    public string RowKey { get; private set; } = "";
    public Guid ProductId { get; private set; }
    public int Position { get; private set; }
    private CatalogImportResult() { }
    public CatalogImportResult(Guid importId, string rowKey, Guid productId, int position)
    { CatalogImportId = importId; RowKey = rowKey; ProductId = productId; Position = position; }
}
