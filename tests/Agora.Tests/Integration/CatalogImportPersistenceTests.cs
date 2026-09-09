using System.Data.Common;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agora.Tests.Integration;

public class CatalogImportPersistenceTests
{
    [Fact]
    public async Task Competing_drafts_for_the_same_identifiers_create_one_complete_batch()
    {
        await using var store = new Store(); var category = await store.Seed(); CatalogImportView a, b;
        await using (var db = store.Context())
        {
            var service = Service(db); var rows = Rows(category, "racing");
            a = await service.PreviewAsync(rows, Guid.NewGuid(), default);
            b = await service.PreviewAsync(rows, Guid.NewGuid(), default);
            Assert.Equal(a.Digest, b.Digest); Assert.NotEqual(a.Id, b.Id);
        }
        var barrier = new Together();
        var outcomes = await Task.WhenAll(new[] { a, b }.Select(draft => Task.Run(async () =>
        {
            await using var db = store.Context(barrier);
            return await Service(db).CommitAsync(draft.Id, draft.Revision, draft.Digest, default);
        })));
        Assert.Single(outcomes, x => x.Status == 200); Assert.Single(outcomes, x => x.Status == 409);
        await using var fresh = store.Context(); Assert.Equal(2, await fresh.Products.CountAsync()); Assert.Equal(2, await fresh.InventoryItems.CountAsync());
        Assert.Equal(2, await fresh.Set<CatalogImportResult>().CountAsync());
        Assert.Equal(1, await fresh.Set<CatalogImport>().CountAsync(x => x.State == CatalogImportState.Applied));
        Assert.Equal(1, await fresh.Set<CatalogImport>().CountAsync(x => x.State == CatalogImportState.DraftValid));
    }

    [Fact]
    public async Task Receipt_insert_failure_rolls_back_products_inventory_and_applied_state_then_retry_succeeds()
    {
        await using var store = new Store(); var category = await store.Seed(); CatalogImportView draft;
        await using (var db = store.Context())
        {
            draft = await Service(db).PreviewAsync(Rows(category, "rollback"), Guid.NewGuid(), default);
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_import_receipt AFTER INSERT ON CatalogImportResults BEGIN SELECT RAISE(ABORT, 'injected receipt failure'); END;");
            await Assert.ThrowsAsync<DbUpdateException>(() => Service(db).CommitAsync(draft.Id, draft.Revision, draft.Digest, default));
        }
        await using (var fresh = store.Context())
        {
            Assert.Empty(await fresh.Products.ToListAsync()); Assert.Empty(await fresh.InventoryItems.ToListAsync()); Assert.Empty(await fresh.Set<CatalogImportResult>().ToListAsync());
            var persisted = await fresh.Set<CatalogImport>().SingleAsync(); Assert.Equal(CatalogImportState.DraftValid, persisted.State); Assert.Equal(0L, persisted.Revision);
            await fresh.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_import_receipt;");
        }
        await using var retry = store.Context(); Assert.Equal(200, (await Service(retry).CommitAsync(draft.Id, draft.Revision, draft.Digest, default)).Status);
    }

    [Fact]
    public async Task Upgrade_preserves_existing_catalog_and_adds_empty_staging_then_receipt_survives_product_deletion()
    {
        await using var store = new Store(); var category = await store.Seed(migrations: true); Guid existingId;
        await using (var db = store.Context())
        {
            var product = new Product { CategoryId = category, Name = "Existing", Slug = "existing", IsActive = true }; existingId = product.Id;
            db.Products.Add(product); await db.SaveChangesAsync();
            await db.GetService<IMigrator>().MigrateAsync("20260908222855_CategoryOptionSchemas");
        }
        await using (var upgraded = store.Context())
        {
            await upgraded.Database.MigrateAsync(); Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
            Assert.True((await upgraded.Products.SingleAsync()).IsActive); Assert.Equal(existingId, (await upgraded.Products.SingleAsync()).Id);
            Assert.Empty(await upgraded.Set<CatalogImport>().ToListAsync());
            var service = Service(upgraded); var draft = await service.PreviewAsync(Rows(category, "upgrade"), Guid.NewGuid(), default);
            var applied = await service.CommitAsync(draft.Id, draft.Revision, draft.Digest, default); Assert.Equal(200, applied.Status);
            var id = applied.Import!.Receipt[0].ProductId;
            upgraded.Products.Remove(await upgraded.Products.SingleAsync(p => p.Id == id)); await upgraded.SaveChangesAsync();
            var replay = await service.CommitAsync(draft.Id, draft.Revision, draft.Digest, default);
            Assert.Equal(applied.Import.Receipt, replay.Import!.Receipt); Assert.Contains(replay.Import.Receipt, r => r.ProductId == id);
        }
    }

    private static CatalogImportService Service(AgoraDbContext db) => new(db,
        new ProductDraftService(db, new CategoryOptionSchemaService(db, NullLogger<CategoryOptionSchemaService>.Instance)),
        TimeProvider.System, new CatalogMutationService(db));
    private static CatalogImportRow[] Rows(Guid category, string prefix) => CatalogImportApiTests.Request(category, prefix).Products
        .Select(row => new CatalogImportRow(row.RowKey, row.Product.ToDraft(true))).ToArray();
    private sealed class Together : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously); private int _arrivals;
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _arrivals);
            if (count <= 2) { if (count == 2) _ready.TrySetResult(true); await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
            return result;
        }
    }
    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-import-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=30");
            if (interceptor is not null) options.AddInterceptors(interceptor); return new AgoraDbContext(options.Options);
        }
        public async Task<Guid> Seed(bool migrations = false)
        {
            await using var db = Context(); if (migrations) await db.Database.MigrateAsync(); else await db.Database.EnsureCreatedAsync();
            var category = new Category { Name = "Import", Slug = "import" }; db.Categories.Add(category); await db.SaveChangesAsync(); return category.Id;
        }
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
}
