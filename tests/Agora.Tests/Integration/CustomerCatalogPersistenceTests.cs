using System.Data.Common;
using System.Security.Claims;
using Agora.Api.Contracts;
using Agora.Api.Controllers;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class CustomerCatalogPersistenceTests
{
    [Fact]
    public async Task Two_creations_at_forty_nine_cannot_exceed_the_saved_search_cap()
    {
        await using var store = new Store(); var data = await store.Seed();
        await using (var seed = store.Context())
        {
            seed.SavedCatalogSearches.AddRange(Enumerable.Range(0, 49).Select(i => new SavedCatalogSearch(data.Reader, $"Search {i}", "{}", DateTimeOffset.UnixEpoch)));
            await seed.SaveChangesAsync();
        }
        var barrier = new StartTogether();
        var tasks = Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
        {
            await using var db = store.Context(barrier);
            var controller = AsUser(new SavedSearchesController(db, TimeProvider.System), data.Reader);
            return (await controller.Create(new CreateSavedSearchRequest("Concurrent " + i, new()), default)).Result;
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, r => r is CreatedAtActionResult);
        Assert.Single(results, r => r is ConflictObjectResult);
        await using var fresh = store.Context(); Assert.Equal(50, await fresh.SavedCatalogSearches.CountAsync());
    }

    [Fact]
    public async Task Concurrent_explicit_views_keep_one_row_with_the_latest_serialized_clock_value()
    {
        await using var store = new Store(); var data = await store.Seed();
        var barrier = new StartTogether(); var clock = new CountingClock();
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var db = store.Context(barrier);
            var controller = AsUser(new RecentProductsController(db, clock), data.Reader);
            return await controller.Record(data.Product, default);
        })).ToArray();
        Assert.All(await Task.WhenAll(tasks), r => Assert.IsType<NoContentResult>(r));
        await using var fresh = store.Context();
        var row = Assert.Single(await fresh.RecentlyViewedProducts.ToListAsync());
        Assert.Equal(DateTimeOffset.UnixEpoch.AddTicks(2), row.LastViewedAt);
    }

    [Fact]
    public async Task Duplicate_report_race_and_stale_resolution_preserve_one_report_and_the_original_review()
    {
        await using var store = new Store(); var data = await store.Seed();
        var barrier = new StartTogether();
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var db = store.Context(barrier);
            var controller = AsUser(new ReviewReportsController(db, TimeProvider.System), data.Reader);
            return (await controller.Create(data.Review, new CreateReviewReportRequest("Spam"), default)).Result;
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, r => r is ObjectResult { StatusCode: 201 });
        Assert.Single(results, r => r is ConflictObjectResult);
        await using var first = store.Context(); await using var stale = store.Context();
        var a = await first.ReviewReports.SingleAsync(); var b = await stale.ReviewReports.SingleAsync();
        a.Resolve(ReviewReportStatus.Resolved, "Winner", data.Author, DateTimeOffset.UtcNow); await first.SaveChangesAsync();
        b.Resolve(ReviewReportStatus.Dismissed, "Loser", data.Author, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var fresh = store.Context();
        var actual = await fresh.ReviewReports.SingleAsync();
        Assert.Equal((ReviewReportStatus.Resolved, "Winner", 1L), (actual.Status, actual.ResolutionNote, actual.Version));
        Assert.Equal(ReviewStatus.Approved, (await fresh.Reviews.SingleAsync()).Status);
    }

    [Fact]
    public async Task Upgrade_preserves_existing_accounts_catalog_and_reviews_and_new_relationships_cascade_as_designed()
    {
        await using var store = new Store(); var data = await store.Seed(migrations: true);
        await using (var old = store.Context())
            await old.GetService<IMigrator>().MigrateAsync("20260908204653_InventoryAdjustmentReceipts");
        await using (var upgraded = store.Context())
        {
            await upgraded.Database.MigrateAsync();
            Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
            Assert.Empty(await upgraded.SavedCatalogSearches.ToListAsync());
            Assert.Empty(await upgraded.RecentlyViewedProducts.ToListAsync());
            Assert.Empty(await upgraded.ReviewReports.ToListAsync());
            Assert.Equal("Original reader", (await upgraded.Customers.SingleAsync(c => c.Id == data.Reader)).FullName);
            Assert.Equal("Original product", (await upgraded.Products.SingleAsync()).Name);
            var review = await upgraded.Reviews.SingleAsync(); Assert.Equal("Original approved review", review.Body); Assert.Equal(ReviewStatus.Approved, review.Status);
            upgraded.AddRange(new SavedCatalogSearch(data.Reader, "Saved", "{}", DateTimeOffset.UtcNow),
                new RecentlyViewedProduct(data.Reader, data.Product, DateTimeOffset.UtcNow),
                new ReviewReport(data.Review, data.Reader, ReviewReportReason.Spam, null, DateTimeOffset.UtcNow));
            await upgraded.SaveChangesAsync();
        }
        await using (var removed = store.Context())
        {
            removed.Customers.Remove(await removed.Customers.SingleAsync(c => c.Id == data.Reader));
            await removed.SaveChangesAsync();
            Assert.Empty(await removed.SavedCatalogSearches.ToListAsync());
            Assert.Empty(await removed.RecentlyViewedProducts.ToListAsync());
            Assert.Empty(await removed.ReviewReports.ToListAsync());
            Assert.Equal(1, await removed.Reviews.CountAsync());
        }
    }

    private static T AsUser<T>(T controller, Guid user) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())], "Test")) } };
        return controller;
    }
    private sealed class CountingClock : TimeProvider
    {
        private long _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(Interlocked.Increment(ref _ticks));
    }
    private sealed class StartTogether : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource<bool> _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            var arrival = Interlocked.Increment(ref _arrivals);
            if (arrival <= 2)
            {
                if (arrival == 2) _both.TrySetResult(true);
                await _both.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
    private sealed record SeedData(Guid Author, Guid Reader, Guid Product, Guid Review);
    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-account-catalog-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=30");
            if (interceptor is not null) options.AddInterceptors(interceptor);
            return new AgoraDbContext(options.Options);
        }
        public async Task<SeedData> Seed(bool migrations = false)
        {
            await using var db = Context();
            if (migrations) await db.Database.MigrateAsync(); else await db.Database.EnsureCreatedAsync();
            var author = new Customer { Email = "author@example.test", FullName = "Original author", PasswordHash = "unused-test-hash" };
            var reader = new Customer { Email = "reader@example.test", FullName = "Original reader", PasswordHash = "unused-test-hash" };
            var category = new Category { Name = "Original category", Slug = "original-category" };
            var product = new Product { Name = "Original product", Slug = "original-product", CategoryId = category.Id };
            var review = new Review(product.Id, author.Id, 4, "Original", "Original approved review"); review.Approve(DateTimeOffset.UnixEpoch);
            db.AddRange(author, reader, category, product, review); await db.SaveChangesAsync();
            return new(author.Id, reader.Id, product.Id, review.Id);
        }
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
}
