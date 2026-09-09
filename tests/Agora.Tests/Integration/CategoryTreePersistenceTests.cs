using System.Data.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class CategoryTreePersistenceTests
{
    [Fact]
    public async Task Competing_moves_that_would_jointly_form_a_cycle_share_one_global_revision()
    {
        await using var store = new Store(); var ids = await store.Seed(); var barrier = new StartTogether();
        var results = await Task.WhenAll(new[] { (ids.A, ids.B), (ids.B, ids.A) }.Select(pair => Task.Run(async () =>
        {
            await using var db = store.Context(barrier);
            try { await new CategoryTreeService(db).MoveAsync(pair.Item1, pair.Item2, 0); return "moved"; }
            catch (CategoryTreeConflictException) { return "stale"; }
        })));
        Assert.Single(results, r => r == "moved"); Assert.Single(results, r => r == "stale");
        await using var fresh = store.Context(); var snapshot = await new CategoryTreeService(fresh).ReadAsync();
        Assert.True(snapshot.IsValid); Assert.Equal(1L, snapshot.Version);
        Assert.Single(snapshot.Nodes, n => n.ParentCategoryId is null); Assert.Single(snapshot.Nodes, n => n.Depth == 2);
    }

    [Fact]
    public async Task Upgrade_adds_revision_without_repairing_legacy_loop_and_explicit_valid_move_can_repair_it()
    {
        await using var store = new Store(); var ids = await store.Seed(migrations: true);
        await using (var legacy = store.Context())
        {
            var nodes = await legacy.Categories.ToListAsync(); nodes.Single(c => c.Id == ids.A).ParentCategoryId = ids.B;
            nodes.Single(c => c.Id == ids.B).ParentCategoryId = ids.A; await legacy.SaveChangesAsync();
            await legacy.GetService<IMigrator>().MigrateAsync("20260908215834_GiftCardLedger");
        }
        await using (var upgraded = store.Context())
        {
            await upgraded.Database.MigrateAsync(); Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
            var service = new CategoryTreeService(upgraded); var invalid = await service.ReadAsync();
            Assert.Equal(0L, invalid.Version); Assert.False(invalid.IsValid); Assert.Contains(invalid.Issues, i => i.Code == "Cycle");
            Assert.Equal(ids.B, invalid.Nodes.Single(n => n.Id == ids.A).ParentCategoryId); Assert.Equal(ids.A, invalid.Nodes.Single(n => n.Id == ids.B).ParentCategoryId);
            await Assert.ThrowsAsync<InvalidCategoryTreeException>(() => service.BreadcrumbsAsync(ids.A));
            await Assert.ThrowsAsync<InvalidCategoryTreeException>(() => service.CreateAsync("Unrelated", "unrelated", null, null));
        }
        await using (var repaired = store.Context())
        {
            var result = await new CategoryTreeService(repaired).MoveAsync(ids.A, null, 0); Assert.Equal(1L, result.Version);
        }
        await using var fresh = store.Context(); var valid = await new CategoryTreeService(fresh).ReadAsync();
        Assert.True(valid.IsValid); Assert.Equal(new[] { ids.A, ids.B }, (await new CategoryTreeService(fresh).BreadcrumbsAsync(ids.B)).Select(n => n.Id));
        Assert.Equal(2, await fresh.Categories.CountAsync());
    }

    [Fact]
    public async Task Stale_global_state_cannot_commit_parent_changes_from_an_independent_context()
    {
        await using var store = new Store(); var ids = await store.Seed();
        await using var first = store.Context(); await using var second = store.Context();
        var firstState = await first.CategoryTreeStates.SingleAsync(); var secondState = await second.CategoryTreeStates.SingleAsync();
        var a = await first.Categories.SingleAsync(c => c.Id == ids.A); var b = await second.Categories.SingleAsync(c => c.Id == ids.B);
        a.ParentCategoryId = ids.B; firstState.Advance(); b.ParentCategoryId = ids.A; secondState.Advance();
        await first.SaveChangesAsync(); await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var fresh = store.Context(); var snapshot = await new CategoryTreeService(fresh).ReadAsync();
        Assert.True(snapshot.IsValid); Assert.Equal(1L, snapshot.Version); Assert.Null(snapshot.Nodes.Single(n => n.Id == ids.B).ParentCategoryId);
    }

    private sealed class StartTogether : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource<bool> _both = new(TaskCreationOptions.RunContinuationsAsynchronously); private int _arrivals;
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            var arrival = Interlocked.Increment(ref _arrivals);
            if (arrival <= 2) { if (arrival == 2) _both.TrySetResult(true); await _both.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
            return result;
        }
    }
    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-category-tree-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=30");
            if (interceptor is not null) options.AddInterceptors(interceptor); return new AgoraDbContext(options.Options);
        }
        public async Task<(Guid A, Guid B)> Seed(bool migrations = false)
        {
            await using var db = Context(); if (migrations) await db.Database.MigrateAsync(); else await db.Database.EnsureCreatedAsync();
            var a = new Category { Name = "A", Slug = "tree-a" }; var b = new Category { Name = "B", Slug = "tree-b" };
            db.Categories.AddRange(a, b); await db.SaveChangesAsync(); return (a.Id, b.Id);
        }
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
}
