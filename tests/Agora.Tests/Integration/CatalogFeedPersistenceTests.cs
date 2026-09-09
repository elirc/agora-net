using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Text.Json;

namespace Agora.Tests.Integration;

public sealed class CatalogFeedPersistenceTests
{
    [Fact]
    public async Task Upgrade_gives_old_products_revision_zero_without_fictional_events()
    {
        await using var store=new Store();await using(var latest=store.Context())await latest.Database.MigrateAsync();var id=await store.Seed();await using(var downgrade=store.Context())await downgrade.GetService<IMigrator>().MigrateAsync("20260908224638_SellingWarehouseAndAccessPolicies");await using var upgraded=store.Context();await upgraded.Database.MigrateAsync();Assert.Equal(0,(await upgraded.Products.SingleAsync(x=>x.Id==id)).CatalogRevision);Assert.Empty(await upgraded.Set<CatalogChange>().ToArrayAsync());var state=await upgraded.Set<CatalogFeedState>().SingleAsync();Assert.Equal((0,0),(state.LastCommittedSequence,state.LastPurgedSequence));
    }

    [Fact]
    public async Task Upsert_delete_and_rollback_keep_product_event_and_watermark_atomic()
    {
        await using var store=new Store();var productId=await store.Seed();
        await using(var update=store.Context())
        {await using var tx=await update.Database.BeginTransactionAsync();var product=await update.Products.SingleAsync(x=>x.Id==productId);product.Name="Feed revision";await new CatalogMutationService(update).StageUpsertAsync(product,DateTimeOffset.UnixEpoch,default);await tx.CommitAsync();}
        await using(var rollback=store.Context())
        {await using var tx=await rollback.Database.BeginTransactionAsync();var product=await rollback.Products.SingleAsync(x=>x.Id==productId);product.Description="must roll back";await new CatalogMutationService(rollback).StageUpsertAsync(product,DateTimeOffset.UnixEpoch.AddMinutes(1),default);await tx.RollbackAsync();}
        await using(var delete=store.Context())
        {await using var tx=await delete.Database.BeginTransactionAsync();var product=await delete.Products.SingleAsync(x=>x.Id==productId);await new CatalogMutationService(delete).StageDeleteAsync(product,DateTimeOffset.UnixEpoch.AddMinutes(2),default);delete.Products.Remove(product);await delete.SaveChangesAsync();await tx.CommitAsync();}
        await using var verify=store.Context();var events=await verify.Set<CatalogChange>().OrderBy(x=>x.Sequence).ToArrayAsync();Assert.Equal(new long[]{1,2},events.Select(x=>x.Sequence));Assert.Equal(CatalogChangeKind.Upsert,events[0].Kind);Assert.Equal(CatalogChangeKind.Delete,events[1].Kind);Assert.Null(events[1].PayloadJson);Assert.False(await verify.Products.AnyAsync(x=>x.Id==productId));Assert.Equal(2,(await verify.Set<CatalogFeedState>().SingleAsync()).LastCommittedSequence);
    }

    [Fact]
    public async Task Mutation_service_refuses_an_unowned_transaction()
    {
        await using var store=new Store();var id=await store.Seed();await using var db=store.Context();var product=await db.Products.SingleAsync(x=>x.Id==id);product.Name="unsafe";await Assert.ThrowsAsync<DomainException>(()=>new CatalogMutationService(db).StageUpsertAsync(product,DateTimeOffset.UtcNow,default));Assert.Equal(0,await db.Set<CatalogChange>().CountAsync());
    }

    [Fact]
    public async Task Bootstrap_and_concurrent_update_serialize_to_a_consistent_checkpoint()
    {
        await using var store = new Store();
        var productId = await store.Seed();
        var barrier = new ProductReadBarrier();
        await using var bootstrapDb = store.Context(barrier);
        await using var updateDb = store.Context();

        var bootstrapTask = new CatalogFeedService(bootstrapDb, TimeProvider.System).BootstrapAsync(default);
        await barrier.ProductIdRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var updateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updateTask = Task.Run(async () =>
        {
            // Signal before BeginTransactionAsync: SQLite's immediate write transaction waits
            // behind the bootstrap read transaction that the interceptor has deliberately paused.
            updateStarted.SetResult();
            await using var transaction = await updateDb.Database.BeginTransactionAsync();
            var product = await updateDb.Products.SingleAsync(candidate => candidate.Id == productId);
            product.Description = "concurrent value";
            await new CatalogMutationService(updateDb).StageUpsertAsync(product, DateTimeOffset.UnixEpoch, default);
            await transaction.CommitAsync();
        });
        try
        {
            await updateStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        var bootstrap = await bootstrapTask;
        await updateTask;
        Assert.Equal(0, bootstrap.Watermark);
        Assert.Equal("old", bootstrap.Products.Single(candidate => candidate.Id == productId).Description);

        await using var verify = store.Context();
        var page = await new CatalogFeedService(verify, TimeProvider.System)
            .ChangesAsync(bootstrap.Watermark, 100, default);
        var change = Assert.Single(page.Changes);
        Assert.Equal("concurrent value", change.Product!.Description);
    }

    [Fact]
    public async Task Purge_stops_at_recent_barrier_and_expires_older_cursor()
    {
        await using var store=new Store();var id=await store.Seed();var now=DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        foreach(var at in new[]{now.AddDays(-40),now.AddDays(-10),now.AddDays(-40)})
        {await using var db=store.Context();await using var tx=await db.Database.BeginTransactionAsync();var product=await db.Products.SingleAsync(x=>x.Id==id);product.Description=Guid.NewGuid().ToString();await new CatalogMutationService(db).StageUpsertAsync(product,at,default);await tx.CommitAsync();}
        await using(var db=store.Context()){var purge=await new CatalogFeedService(db,new FixedClock(now)).PurgeAsync(default);Assert.Equal(1,purge.PurgedCount);Assert.Equal(1,purge.RetentionFloor);}
        await using var verify=store.Context();Assert.Equal(new long[]{2,3},await verify.Set<CatalogChange>().OrderBy(x=>x.Sequence).Select(x=>x.Sequence).ToArrayAsync());await Assert.ThrowsAsync<CatalogCursorException>(()=>new CatalogFeedService(verify,new FixedClock(now)).ChangesAsync(0,100,default));
    }

    [Fact]
    public async Task Legacy_oversized_product_is_rejected_in_bootstrap_without_truncation()
    {
        await using var store=new Store();var id=await store.Seed();await using(var db=store.Context()){var variants=Enumerable.Range(0,3000).Select(i=>new ProductVariant{ProductId=id,Sku=$"LEGACY-{i}",Name=new string('x',120),Price=new Money(1),Options=new(){["detail"]=new string('y',80)}}).ToArray();db.ProductVariants.AddRange(variants);await db.SaveChangesAsync();}
        await using var read=store.Context();await Assert.ThrowsAsync<CatalogSnapshotTooLargeException>(()=>new CatalogFeedService(read,TimeProvider.System).BootstrapAsync(default));
    }

    [Fact]
    public async Task Bootstrap_rejects_more_than_one_thousand_products()
    {
        await using var store = new Store();
        await store.SeedProducts(1001, "small");

        await using var read = store.Context();
        await Assert.ThrowsAsync<CatalogSnapshotTooLargeException>(() =>
            new CatalogFeedService(read, TimeProvider.System).BootstrapAsync(default));
    }

    [Fact]
    public async Task Bootstrap_stops_loading_products_as_soon_as_the_five_mibibyte_budget_is_exhausted()
    {
        await using var store = new Store();
        await store.SeedProducts(40, new string('d', 180_000));
        var counter = new CommandCounter();

        await using var read = store.Context(counter);
        await Assert.ThrowsAsync<CatalogSnapshotTooLargeException>(() =>
            new CatalogFeedService(read, TimeProvider.System).BootstrapAsync(default));

        // A complete graph uses several split queries. This bound proves the service did not load
        // all forty large graphs before discovering that the response could not fit.
        Assert.True(counter.ReaderCommands < 100, $"Executed {counter.ReaderCommands} reader commands.");
    }

    [Fact]
    public async Task Bootstrap_accepts_exactly_five_mibibytes_and_rejects_one_more_byte()
    {
        const int limit = 5 * 1024 * 1024;
        await using var store = new Store();
        await store.SeedProducts(22, string.Empty);
        await using (var arrange = store.Context())
        {
            var products = await arrange.Products.Include(product => product.Variants)
                .Include(product => product.Images).OrderBy(product => product.Id).ToListAsync();
            var empty = new CatalogBootstrapResult(0,
                products.Select(CatalogMutationService.Snapshot).ToArray());
            var remaining = limit - JsonSerializer.SerializeToUtf8Bytes(empty,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length;
            foreach (var product in products)
            {
                var bytes = Math.Min(240_000, remaining);
                product.Description = new string('x', bytes);
                remaining -= bytes;
            }
            Assert.Equal(0, remaining);
            await arrange.SaveChangesAsync();
        }

        await using (var exact = store.Context())
        {
            var result = await new CatalogFeedService(exact, TimeProvider.System).BootstrapAsync(default);
            Assert.Equal(limit, JsonSerializer.SerializeToUtf8Bytes(result,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length);
        }
        await using (var oneMore = store.Context())
        {
            var product = await oneMore.Products.OrderBy(candidate => candidate.Id).FirstAsync();
            product.Description += "x";
            await oneMore.SaveChangesAsync();
        }
        await using var rejected = store.Context();
        await Assert.ThrowsAsync<CatalogSnapshotTooLargeException>(() =>
            new CatalogFeedService(rejected, TimeProvider.System).BootstrapAsync(default));
    }

    [Fact]
    public async Task Changes_returns_a_replayable_byte_bounded_prefix()
    {
        await using var store = new Store();
        var productId = await store.Seed();
        await using (var seed = store.Context())
        {
            var state = await seed.Set<CatalogFeedState>().SingleAsync();
            for (var revision = 1; revision <= 5; revision++)
            {
                var snapshot = new CatalogProductSnapshot(1, productId, revision, Guid.NewGuid(), null,
                    "Large", $"large-{revision}", new string('x', 220_000), true,
                    DateTimeOffset.UnixEpoch, [], []);
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                seed.Set<CatalogChange>().Add(new CatalogChange(productId, revision,
                    CatalogChangeKind.Upsert, json, System.Text.Encoding.UTF8.GetByteCount(json),
                    DateTimeOffset.UnixEpoch));
            }
            await seed.SaveChangesAsync();
            var last = await seed.Set<CatalogChange>().MaxAsync(change => change.Sequence);
            state.Commit(last);
            await seed.SaveChangesAsync();
        }

        await using var read = store.Context();
        var service = new CatalogFeedService(read, TimeProvider.System);
        var first = await service.ChangesAsync(0, 100, default);
        var encoded = JsonSerializer.SerializeToUtf8Bytes(first,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.InRange(encoded.Length, 1, 1024 * 1024);
        Assert.InRange(first.Changes.Count, 1, 4);
        Assert.Equal(Enumerable.Range(1, first.Changes.Count).Select(value => (long)value),
            first.Changes.Select(change => change.Sequence));

        var second = await service.ChangesAsync(first.LastDeliveredSequence, 100, default);
        Assert.Equal(5 - first.Changes.Count, second.Changes.Count);
        Assert.Equal(5, second.LastDeliveredSequence);
    }

    [Fact]
    public async Task Oversized_upsert_rolls_back_the_product_revision_event_and_watermark()
    {
        await using var store = new Store();
        var id = await store.Seed();
        await using (var write = store.Context())
        {
            await using var transaction = await write.Database.BeginTransactionAsync();
            var product = await write.Products.SingleAsync(candidate => candidate.Id == id);
            product.Description = new string('x', 300_000);
            await Assert.ThrowsAsync<CatalogSnapshotTooLargeException>(() =>
                new CatalogMutationService(write).StageUpsertAsync(product, DateTimeOffset.UnixEpoch, default));
            await transaction.RollbackAsync();
        }

        await using var verify = store.Context();
        var unchanged = await verify.Products.SingleAsync(candidate => candidate.Id == id);
        Assert.Equal("old", unchanged.Description);
        Assert.Equal(0, unchanged.CatalogRevision);
        Assert.Empty(await verify.Set<CatalogChange>().ToArrayAsync());
        Assert.Equal(0, (await verify.Set<CatalogFeedState>().SingleAsync()).LastCommittedSequence);
    }

    [Fact]
    public async Task Changes_accepts_an_exact_one_mibibyte_prefix_with_one_batched_payload_query()
    {
        await using var store = new Store();
        var productId = await store.Seed();
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshots = Enumerable.Range(1, 5).Select(revision => new CatalogProductSnapshot(
            1, productId, revision, Guid.NewGuid(), null, "Boundary", $"boundary-{revision}", "",
            true, DateTimeOffset.UnixEpoch, [], [])).ToArray();
        int Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, jsonOptions).Length;

        for (var index = 0; index < 3; index++)
            snapshots[index] = snapshots[index] with { Description = new string('x', 262_000 - Bytes(snapshots[index])) };

        CatalogChangesResult FirstFour() => new(0, 4, 5, 0,
            snapshots.Take(4).Select((snapshot, index) => new CatalogChangeResult(
                index + 1, productId, index + 1, "Upsert", 1, snapshot)).ToArray());
        snapshots[3] = snapshots[3] with { Description = new string('x', 1024 * 1024 - Bytes(FirstFour())) };
        Assert.Equal(1024 * 1024, Bytes(FirstFour()));
        Assert.All(snapshots, snapshot => Assert.InRange(Bytes(snapshot), 1, 256 * 1024));

        await using (var seed = store.Context())
        {
            foreach (var snapshot in snapshots)
            {
                var payload = JsonSerializer.Serialize(snapshot, jsonOptions);
                seed.Set<CatalogChange>().Add(new CatalogChange(productId, snapshot.Revision,
                    CatalogChangeKind.Upsert, payload, Bytes(snapshot), DateTimeOffset.UnixEpoch));
                await seed.SaveChangesAsync();
            }
            var state = await seed.Set<CatalogFeedState>().SingleAsync();
            state.Commit(5);
            await seed.SaveChangesAsync();
        }

        var commands = new CommandCounter();
        await using var read = store.Context(commands);
        var service = new CatalogFeedService(read, TimeProvider.System);
        var first = await service.ChangesAsync(0, 100, default);
        Assert.Equal(4, first.Changes.Count);
        Assert.Equal(1024 * 1024, Bytes(first));
        Assert.Equal(3, commands.ReaderCommands); // State, size metadata, then one payload batch.
        var second = await service.ChangesAsync(first.LastDeliveredSequence, 100, default);
        Assert.Equal(5, Assert.Single(second.Changes).Sequence);
    }

    [Fact]
    public async Task Purge_removes_at_most_one_thousand_and_new_sequences_never_reuse_purged_values()
    {
        await using var store = new Store();
        var productId = await store.Seed();
        var now = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        await using (var seed = store.Context())
        {
            var state = await seed.Set<CatalogFeedState>().SingleAsync();
            for (var revision = 1; revision <= 1002; revision++)
                seed.Set<CatalogChange>().Add(new CatalogChange(productId, revision,
                    CatalogChangeKind.Delete, null, 0, now.AddDays(-31)));
            await seed.SaveChangesAsync();
            state.Commit(await seed.Set<CatalogChange>().MaxAsync(change => change.Sequence));
            await seed.SaveChangesAsync();
        }

        await using (var first = store.Context())
        {
            var result = await new CatalogFeedService(first, new FixedClock(now)).PurgeAsync(default);
            Assert.Equal(1000, result.PurgedCount);
            Assert.Equal(1000, result.RetentionFloor);
        }
        await using (var second = store.Context())
        {
            var result = await new CatalogFeedService(second, new FixedClock(now)).PurgeAsync(default);
            Assert.Equal(2, result.PurgedCount);
            Assert.Equal(1002, result.RetentionFloor);
        }
        await using (var write = store.Context())
        {
            await using var transaction = await write.Database.BeginTransactionAsync();
            var product = await write.Products.SingleAsync(candidate => candidate.Id == productId);
            product.Name = "After purge";
            var change = await new CatalogMutationService(write)
                .StageUpsertAsync(product, now, default);
            await transaction.CommitAsync();
            Assert.Equal(1003, change.Sequence);
        }
    }

    [Fact]
    public async Task Purge_treats_the_exact_thirty_day_boundary_as_a_retention_barrier()
    {
        await using var store = new Store();
        var productId = await store.Seed();
        var now = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        await using (var seed = store.Context())
        {
            var state = await seed.Set<CatalogFeedState>().SingleAsync();
            seed.Set<CatalogChange>().AddRange(
                new CatalogChange(productId, 1, CatalogChangeKind.Delete, null, 0, now.AddDays(-31)),
                new CatalogChange(productId, 2, CatalogChangeKind.Delete, null, 0, now.AddDays(-30)),
                new CatalogChange(productId, 3, CatalogChangeKind.Delete, null, 0, now.AddDays(-31)));
            await seed.SaveChangesAsync();
            state.Commit(3);
            await seed.SaveChangesAsync();
        }

        await using var purge = store.Context();
        var result = await new CatalogFeedService(purge, new FixedClock(now)).PurgeAsync(default);
        Assert.Equal(1, result.PurgedCount);
        Assert.Equal(1, result.RetentionFloor);
        Assert.Equal(new long[] { 2, 3 },
            await purge.Set<CatalogChange>().OrderBy(change => change.Sequence)
                .Select(change => change.Sequence).ToArrayAsync());
    }

    private sealed class FixedClock(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}
    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int ReaderCommands { get; private set; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            ReaderCommands++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommands++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ProductReadBarrier : DbCommandInterceptor
    {
        private int paused;
        public TaskCompletionSource ProductIdRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Products\"", StringComparison.Ordinal)
                && Interlocked.Exchange(ref paused, 1) == 0)
            {
                ProductIdRead.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"agora-feed-{Guid.NewGuid():N}.db");

        public AgoraDbContext Context(params IInterceptor[] interceptors)
        {
            var options = new DbContextOptionsBuilder<AgoraDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=30");
            if (interceptors.Length > 0) options.AddInterceptors(interceptors);
            return new AgoraDbContext(options.Options);
        }

        public async Task<Guid> Seed()
        {
            await using var db = Context();
            await db.Database.EnsureCreatedAsync();
            var category = new Category { Name = "Feed", Slug = "feed" };
            var product = new Product
            {
                CategoryId = category.Id,
                Name = "Feed product",
                Slug = "feed-product",
                Description = "old"
            };
            product.Variants.Add(new ProductVariant
            {
                ProductId = product.Id,
                Sku = "FEED-1",
                Name = "Base",
                Price = new Money(10),
                WeightGrams = 50,
                Options = new() { ["size"] = "M" }
            });
            db.AddRange(category, product);
            await db.SaveChangesAsync();
            return product.Id;
        }

        public async Task SeedProducts(int count, string description)
        {
            await using var db = Context();
            await db.Database.EnsureCreatedAsync();
            var category = new Category { Name = "Bulk feed", Slug = $"bulk-feed-{Guid.NewGuid():N}" };
            db.Categories.Add(category);
            for (var index = 0; index < count; index++)
                db.Products.Add(new Product
                {
                    CategoryId = category.Id,
                    Name = $"Bulk {index}",
                    Slug = $"bulk-{index}-{Guid.NewGuid():N}",
                    Description = description
                });
            await db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(path);
            return ValueTask.CompletedTask;
        }
    }
}
