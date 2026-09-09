using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Services;

public sealed record CatalogImportRow(string RowKey, ProductDraftInput Product);
public sealed record CatalogImportError(string RowKey, string Field, string Code, string Message);
public sealed record CatalogImportReceipt(string RowKey, Guid ProductId);
public sealed record CatalogImportView(Guid Id, int Version, string State, long Revision, string Digest,
    Guid AuthorId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? AppliedAt,
    IReadOnlyList<CatalogImportRow> Products, IReadOnlyList<CatalogImportError> Errors,
    IReadOnlyList<CatalogImportReceipt> Receipt);
public sealed record CatalogImportCommitResult(int Status, CatalogImportView? Import, string? Error = null,
    IReadOnlyList<CatalogImportError>? RowErrors = null);

public sealed class CatalogImportService(AgoraDbContext db, ProductDraftService products,
    TimeProvider clock, CatalogMutationService catalogFeed)
{
    public async Task<CatalogImportView> PreviewAsync(IReadOnlyList<CatalogImportRow> rows, Guid author, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var (errors, _) = await ValidateAsync(rows, ct);
        var json = JsonSerializer.Serialize(rows);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var draft = new CatalogImport(json, digest, JsonSerializer.Serialize(errors), errors.Count == 0, author, clock.GetUtcNow());
        db.Set<CatalogImport>().Add(draft);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return View(draft);
    }

    public async Task<CatalogImportView?> GetAsync(Guid id, CancellationToken ct)
    {
        var draft = await db.Set<CatalogImport>().AsNoTracking().Include(x => x.Results).SingleOrDefaultAsync(x => x.Id == id, ct);
        return draft is null ? null : View(draft);
    }

    public async Task<CatalogImportCommitResult> CommitAsync(Guid id, long revision, string digest, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var draft = await db.Set<CatalogImport>().Include(x => x.Results).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (draft is null) return new(404, null, "Import does not exist.");
        if (!string.Equals(draft.Digest, digest, StringComparison.Ordinal)) return new(409, null, "The proposal digest does not match.");
        // An applied receipt is a historical fact. Retrying the original revision is intentional.
        if (draft.State == CatalogImportState.Applied) return new(200, View(draft));
        if (draft.Revision != revision || draft.State != CatalogImportState.DraftValid || clock.GetUtcNow() >= draft.ExpiresAt)
            return new(409, null, "The draft is stale, invalid, or expired. Create a new preview.");
        if (draft.ProposalVersion != 1) return new(409, null, "This proposal version cannot be committed.");
        var rows = JsonSerializer.Deserialize<CatalogImportRow[]>(draft.ProposalJson)!;
        var (errors, graphs) = await ValidateAsync(rows, ct);
        if (errors.Count != 0) return new(409, null, "The live catalog no longer accepts this proposal. No products were created.", errors);
        var now = clock.GetUtcNow();
        for (var i = 0; i < graphs.Count; i++)
        {
            graphs[i].CreatedAt = now;
            db.Products.Add(graphs[i]);
        }
        var receipts = graphs.Select((product, index) => new CatalogImportResult(draft.Id, rows[index].RowKey, product.Id, index)).ToArray();
        draft.Apply(receipts, now);
        // Explicit Added state avoids treating client-generated child keys as existing rows.
        db.Set<CatalogImportResult>().AddRange(receipts);
        try
        {
            await db.SaveChangesAsync(ct);
            foreach (var product in graphs)
                await catalogFeed.StageUpsertAsync(product, now, ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new(409, null, "A catalog identifier was claimed concurrently. No products were created.");
        }
        return new(200, View(draft));
    }

    private async Task<(List<CatalogImportError> Errors, List<Product> Products)> ValidateAsync(IReadOnlyList<CatalogImportRow> rows, CancellationToken ct)
    {
        var errors = new List<CatalogImportError>();
        var graphs = new List<Product>();
        var duplicateKeys = rows.GroupBy(x => x.RowKey, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var duplicateSlugs = rows.GroupBy(x => x.Product.Slug, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var duplicateSkus = rows.SelectMany(x => x.Product.Variants).GroupBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (duplicateKeys.Contains(row.RowKey)) errors.Add(new(row.RowKey, "rowKey", "DuplicateRowKey", "Row keys must be unique after trimming."));
            if (duplicateSlugs.Contains(row.Product.Slug)) errors.Add(new(row.RowKey, "slug", "DuplicateSlug", "The batch contains this slug more than once."));
            if (row.Product.Variants.Any(v => duplicateSkus.Contains(v.Sku))) errors.Add(new(row.RowKey, "variants", "DuplicateSku", "The batch contains this SKU more than once (ignoring case)."));
            try
            {
                var validation = await products.ValidateAndBuildAsync(row.Product with { IsActive = false }, ct);
                errors.AddRange(validation.Errors.Select(e => new CatalogImportError(row.RowKey, e.Field, e.Code, e.Message)));
                if (validation.Product is not null) graphs.Add(validation.Product);
            }
            catch (InvalidCategoryOptionsException ex)
            {
                foreach (var variant in ex.Violations)
                    foreach (var violation in variant.Violations)
                        errors.Add(new(row.RowKey, $"variants[{variant.Sku}].options.{violation.Key}", violation.Reason, "The category option schema rejects this option."));
            }
            catch (CategoryOptionSchemaStateException)
            {
                errors.Add(new(row.RowKey, "categoryId", "UnreadableSchema", "Repair the category option schema before importing."));
            }
        }
        return (errors, graphs);
    }

    private static CatalogImportView View(CatalogImport draft) => new(draft.Id, draft.ProposalVersion, draft.State.ToString(),
        draft.Revision, draft.Digest, draft.AuthorId, draft.CreatedAt, draft.ExpiresAt, draft.AppliedAt,
        JsonSerializer.Deserialize<CatalogImportRow[]>(draft.ProposalJson)!,
        JsonSerializer.Deserialize<CatalogImportError[]>(draft.ErrorsJson)!,
        draft.Results.OrderBy(r => r.Position).Select(r => new CatalogImportReceipt(r.RowKey, r.ProductId)).ToArray());
}
