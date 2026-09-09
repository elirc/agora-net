using System.Text;
using System.Text.Json;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record CatalogOptionSnapshot(string Key, string Value);
public sealed record CatalogVariantSnapshot(Guid Id, string Sku, string Name, decimal BasePrice,
    string Currency, int WeightGrams, IReadOnlyList<CatalogOptionSnapshot> Options);
public sealed record CatalogImageSnapshot(Guid Id, string Url, string? AltText, int SortOrder);
public sealed record CatalogProductSnapshot(int Version, Guid Id, long Revision, Guid CategoryId,
    Guid? TaxCategoryId, string Name, string Slug, string Description, bool IsActive,
    DateTimeOffset CreatedAt, IReadOnlyList<CatalogVariantSnapshot> Variants,
    IReadOnlyList<CatalogImageSnapshot> Images);
public sealed record CatalogBootstrapResult(long Watermark, IReadOnlyList<CatalogProductSnapshot> Products);
public sealed record CatalogChangeResult(long Sequence, Guid ProductId, long ProductRevision,
    string Kind, int PayloadVersion, CatalogProductSnapshot? Product);
public sealed record CatalogChangesResult(long After, long LastDeliveredSequence, long HighWatermark,
    long RetentionFloor, IReadOnlyList<CatalogChangeResult> Changes);
public sealed record CatalogPurgeResult(int PurgedCount, long RetentionFloor, long HighWatermark);

public sealed class CatalogMutationService(AgoraDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CatalogChange> StageUpsertAsync(Product product, DateTimeOffset now, CancellationToken ct)
    {
        RequireTransaction();
        await db.SaveChangesAsync(ct); // Persist pending children/deletions before reading the canonical graph.
        product.AdvanceCatalogRevision();
        await db.SaveChangesAsync(ct);

        var canonical = await CompleteProducts(db.Products.AsNoTracking())
            .SingleAsync(candidate => candidate.Id == product.Id, ct);
        var snapshot = Snapshot(canonical);
        var payload = BoundedJson.Serialize(snapshot, 256 * 1024,
            "A product snapshot exceeds the 256 KiB feed limit.", Json);
        var json = Encoding.UTF8.GetString(payload);
        var bytes = payload.Length;

        var change = new CatalogChange(product.Id, product.CatalogRevision,
            CatalogChangeKind.Upsert, json, bytes, now);
        db.Set<CatalogChange>().Add(change);
        await db.SaveChangesAsync(ct); // Obtain the SQLite AUTOINCREMENT sequence.
        await AdvanceHighWatermark(change.Sequence, ct);
        return change;
    }

    public async Task<CatalogChange> StageDeleteAsync(Product product, DateTimeOffset now, CancellationToken ct)
    {
        RequireTransaction();
        await db.SaveChangesAsync(ct);
        product.AdvanceCatalogRevision();
        var change = new CatalogChange(product.Id, product.CatalogRevision,
            CatalogChangeKind.Delete, null, 0, now);
        db.Set<CatalogChange>().Add(change);
        await db.SaveChangesAsync(ct);
        await AdvanceHighWatermark(change.Sequence, ct);
        return change;
    }

    private async Task AdvanceHighWatermark(long sequence, CancellationToken ct)
    {
        var state = await db.Set<CatalogFeedState>().SingleAsync(x => x.Id == CatalogFeedState.SingletonId, ct);
        state.Commit(sequence);
        await db.SaveChangesAsync(ct);
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new DomainException("Catalog changes must be staged inside the business write transaction.");
    }

    internal static IQueryable<Product> CompleteProducts(IQueryable<Product> query) =>
        query.AsSplitQuery().Include(product => product.Variants).Include(product => product.Images);

    public static CatalogProductSnapshot Snapshot(Product product) => new(
        1, product.Id, product.CatalogRevision, product.CategoryId, product.TaxCategoryId,
        product.Name, product.Slug, product.Description, product.IsActive, product.CreatedAt,
        product.Variants.OrderBy(variant => variant.Id).Select(variant => new CatalogVariantSnapshot(
            variant.Id, variant.Sku, variant.Name, variant.Price.Amount, variant.Price.Currency,
            variant.WeightGrams, variant.Options.OrderBy(option => option.Key, StringComparer.Ordinal)
                .Select(option => new CatalogOptionSnapshot(option.Key, option.Value)).ToArray())).ToArray(),
        product.Images.OrderBy(image => image.SortOrder).ThenBy(image => image.Id)
            .Select(image => new CatalogImageSnapshot(image.Id, image.Url, image.AltText, image.SortOrder)).ToArray());
}

public sealed class CatalogFeedService(AgoraDbContext db, TimeProvider clock)
{
    private const int BootstrapBytes = 5 * 1024 * 1024;
    private const int ChangesBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CatalogBootstrapResult> BootstrapAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = await State(ct);
        var productIds = await db.Products.AsNoTracking().OrderBy(product => product.Id)
            .Select(product => product.Id).Take(1001).ToArrayAsync(ct);
        if (productIds.Length > 1000)
            throw new CatalogSnapshotTooLargeException("Bootstrap exceeds 1,000 products; use a future large export.");

        var snapshots = new List<CatalogProductSnapshot>(productIds.Length);
        var retainedBytes = BoundedJson.Measure(
            new CatalogBootstrapResult(state.LastCommittedSequence, []),
            BootstrapBytes,
            "Bootstrap exceeds 5 MiB; use a future large export.",
            Json);
        foreach (var productId in productIds)
        {
            var product = await CatalogMutationService.CompleteProducts(db.Products.AsNoTracking())
                .SingleAsync(candidate => candidate.Id == productId, ct);
            var snapshot = CatalogMutationService.Snapshot(product);
            var snapshotBytes = BoundedJson.Measure(snapshot, 256 * 1024,
                "A legacy product snapshot exceeds 256 KiB and cannot be bootstrapped.", Json);
            var separatorBytes = snapshots.Count == 0 ? 0 : 1;
            if (retainedBytes + snapshotBytes + separatorBytes > BootstrapBytes)
                throw new CatalogSnapshotTooLargeException("Bootstrap exceeds 5 MiB; use a future large export.");
            retainedBytes += snapshotBytes + separatorBytes;
            snapshots.Add(snapshot);
        }
        var result = new CatalogBootstrapResult(state.LastCommittedSequence, snapshots);
        BoundedJson.Measure(result, BootstrapBytes,
            "Bootstrap exceeds 5 MiB; use a future large export.", Json);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<CatalogChangesResult> ChangesAsync(long after, int limit, CancellationToken ct)
    {
        if (after < 0 || limit is < 1 or > 100)
            throw new CatalogCursorException("Use after >= 0 and limit from 1 to 100.", false);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = await State(ct);
        if (after < state.LastPurgedSequence)
            throw new CatalogCursorException("This cursor expired; request a new bootstrap.", true);
        if (after > state.LastCommittedSequence)
            throw new CatalogCursorException("The cursor is ahead of the committed watermark.", false);

        var metadata = await db.Set<CatalogChange>().AsNoTracking()
            .Where(change => change.Sequence > after)
            .OrderBy(change => change.Sequence)
            .Take(limit)
            .Select(change => new
            {
                change.Sequence, change.ProductId, change.ProductRevision,
                change.Kind, change.PayloadVersion, change.PayloadByteCount
            })
            .ToArrayAsync(ct);
        var selected = new List<long>(metadata.Length);
        var retainedRowBytes = 0;
        foreach (var row in metadata)
        {
            // Measure only metadata. An Upsert replaces the four bytes of JSON
            // null with its canonical payload; Delete retains that null value.
            var emptyRow = new CatalogChangeResult(row.Sequence, row.ProductId,
                row.ProductRevision, row.Kind.ToString(), row.PayloadVersion, null);
            var rowBytes = BoundedJson.Measure(emptyRow, ChangesBytes,
                "Catalog change metadata exceeds its page budget.", Json);
            if (row.Kind == CatalogChangeKind.Upsert)
                rowBytes = checked(rowBytes - 4 + row.PayloadByteCount);

            var emptyWrapper = new CatalogChangesResult(after, row.Sequence,
                state.LastCommittedSequence, state.LastPurgedSequence, []);
            var wrapperBytes = BoundedJson.Measure(emptyWrapper, ChangesBytes,
                "Catalog change metadata exceeds its page budget.", Json);
            var candidateRowBytes = checked(retainedRowBytes + rowBytes + (selected.Count == 0 ? 0 : 1));
            if (wrapperBytes + candidateRowBytes > ChangesBytes)
            {
                if (selected.Count == 0)
                    throw new CatalogSnapshotTooLargeException("One catalog change exceeds the 1 MiB page budget.");
                break;
            }
            selected.Add(row.Sequence);
            retainedRowBytes = candidateRowBytes;
        }

        // Load only the fitting prefix, together, after its sizes are known.
        var rows = await db.Set<CatalogChange>().AsNoTracking()
            .Where(change => selected.Contains(change.Sequence))
            .OrderBy(change => change.Sequence).ToArrayAsync(ct);
        var changes = rows.Select(row => new CatalogChangeResult(row.Sequence, row.ProductId,
            row.ProductRevision, row.Kind.ToString(), row.PayloadVersion,
            row.PayloadJson is null ? null : JsonSerializer.Deserialize<CatalogProductSnapshot>(row.PayloadJson, Json))).ToArray();
        var result = new CatalogChangesResult(after, changes.LastOrDefault()?.Sequence ?? after,
            state.LastCommittedSequence, state.LastPurgedSequence, changes);
        BoundedJson.Measure(result, ChangesBytes, "The catalog change page exceeds 1 MiB.", Json);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<CatalogPurgeResult> PurgeAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = await db.Set<CatalogFeedState>().SingleAsync(x => x.Id == CatalogFeedState.SingletonId, ct);
        var cutoff = clock.GetUtcNow().AddDays(-30);
        var prefix = await db.Set<CatalogChange>().Where(change => change.Sequence > state.LastPurgedSequence)
            .OrderBy(change => change.Sequence).Take(1000)
            .Select(change => new { change.Sequence, change.CreatedAt }).ToArrayAsync(ct);
        var purgeThrough = prefix.TakeWhile(change => change.CreatedAt < cutoff).LastOrDefault()?.Sequence;
        var count = 0;
        if (purgeThrough.HasValue)
        {
            count = await db.Set<CatalogChange>()
                .Where(change => change.Sequence > state.LastPurgedSequence && change.Sequence <= purgeThrough.Value)
                .ExecuteDeleteAsync(ct);
            state.PurgeThrough(purgeThrough.Value);
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new CatalogPurgeResult(count, state.LastPurgedSequence, state.LastCommittedSequence);
    }

    private Task<CatalogFeedState> State(CancellationToken ct) => db.Set<CatalogFeedState>().AsNoTracking()
        .SingleAsync(x => x.Id == CatalogFeedState.SingletonId, ct);
}

internal static class BoundedJson
{
    public static byte[] Serialize<T>(T value,int limit,string message,JsonSerializerOptions options)
    {
        using var stream=new LimitedStream(limit,message,keepBytes:true);
        JsonSerializer.Serialize(stream,value,options);
        return stream.ToArray();
    }
    public static int Measure<T>(T value,int limit,string message,JsonSerializerOptions options)
    {
        using var stream=new LimitedStream(limit,message,keepBytes:false);
        JsonSerializer.Serialize(stream,value,options);
        return checked((int)stream.Length);
    }
    private sealed class LimitedStream(int limit,string message,bool keepBytes):Stream
    {
        private readonly MemoryStream? buffer=keepBytes?new():null;
        private long length;
        public byte[] ToArray()=>buffer?.ToArray()??[];
        public override void Write(byte[] bytes,int offset,int count){Add(count);buffer?.Write(bytes,offset,count);}
        public override void Write(ReadOnlySpan<byte> bytes){Add(bytes.Length);buffer?.Write(bytes);}
        private void Add(int count){if(length+count>limit)throw new CatalogSnapshotTooLargeException(message);length+=count;}
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>length;public override long Position{get=>length;set=>throw new NotSupportedException();}
        public override void Flush(){} public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();
    }
}
