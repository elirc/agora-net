using System.Data.Common;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class CartTemplatePersistenceTests
{
    [Fact]
    public async Task Two_creations_at_nine_serialize_the_capacity_check_and_only_one_wins()
    {
        await using var store = new Store(); var seed = await store.Seed();
        await using (var db = store.Context())
        {
            db.CartTemplates.AddRange(Enumerable.Range(0, 9).Select(i => new CartTemplate(seed.Owner, "Template " + i,
                [new(seed.Variant, 1, "SKU", "Product", "Variant")], DateTimeOffset.UnixEpoch)));
            await db.SaveChangesAsync();
        }
        var barrier = new StartTogether();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
        {
            await using var db = store.Context(barrier);
            try { await new CartTemplateService(db, TimeProvider.System).CreateAsync(seed.Owner, "Concurrent " + i, seed.Token); return "created"; }
            catch (CartTemplateConflictException) { return "capacity"; }
        })));
        Assert.Single(results, r => r == "created"); Assert.Single(results, r => r == "capacity");
        await using var fresh = store.Context(); Assert.Equal(10, await fresh.CartTemplates.CountAsync());
        Assert.Equal(10, await fresh.CartTemplateLines.CountAsync());
    }

    [Fact]
    public async Task Upgrade_preserves_cart_and_saved_search_and_historical_lines_survive_variant_deletion()
    {
        await using var store = new Store(); var seed = await store.Seed(migrations: true);
        await using (var old = store.Context())
        {
            old.SavedCatalogSearches.Add(new SavedCatalogSearch(seed.Owner, "Existing search", "{}", DateTimeOffset.UnixEpoch)); await old.SaveChangesAsync();
            await old.GetService<IMigrator>().MigrateAsync("20260908211353_CustomerCatalogWorkflows");
        }
        Guid templateId;
        await using (var upgraded = store.Context())
        {
            await upgraded.Database.MigrateAsync(); Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
            Assert.Empty(await upgraded.CartTemplates.ToListAsync());
            Assert.Equal("Existing search", (await upgraded.SavedCatalogSearches.SingleAsync()).Name);
            var cart = await upgraded.Carts.Include(c => c.Items).SingleAsync(); Assert.Equal(seed.Token, cart.Token); Assert.Equal(2, cart.Items.Single().Quantity);
            var template = await new CartTemplateService(upgraded, TimeProvider.System).CreateAsync(seed.Owner, "Historical", seed.Token); templateId = template.Id;
        }
        await using (var deleted = store.Context())
        {
            deleted.ProductVariants.Remove(await deleted.ProductVariants.SingleAsync()); await deleted.SaveChangesAsync();
            var line = await deleted.CartTemplateLines.SingleAsync();
            Assert.Equal(seed.Variant, line.VariantId); Assert.Equal("ORIGINAL-SKU", line.Sku); Assert.Equal(templateId, line.CartTemplateId);
        }
        await using (var deletedOwner = store.Context())
        {
            deletedOwner.Customers.Remove(await deletedOwner.Customers.SingleAsync()); await deletedOwner.SaveChangesAsync();
            Assert.Empty(await deletedOwner.CartTemplates.ToListAsync()); Assert.Empty(await deletedOwner.CartTemplateLines.ToListAsync());
        }
    }

    [Fact]
    public async Task Concurrent_applies_using_one_observed_cart_revision_apply_exactly_once()
    {
        await using var store = new Store(); var seed = await store.Seed(); Guid templateId; int version;
        await using (var db = store.Context())
        {
            templateId = (await new CartTemplateService(db, TimeProvider.System).CreateAsync(seed.Owner, "Repeat", seed.Token)).Id;
            version = (await db.Carts.SingleAsync()).Version;
        }
        var barrier = new StartTogether();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var db = store.Context(barrier);
            try { await new CartTemplateService(db, TimeProvider.System).ApplyAsync(seed.Owner, templateId, seed.Token, version); return "applied"; }
            catch (CartTemplateConflictException) { return "stale"; }
        })));
        Assert.Single(results, r => r == "applied"); Assert.Single(results, r => r == "stale");
        await using var fresh = store.Context(); var cart = await fresh.Carts.Include(c => c.Items).SingleAsync();
        Assert.Equal(version + 1, cart.Version); Assert.Equal(4, cart.Items.Single().Quantity);
        Assert.Equal(2, (await fresh.CartTemplateLines.SingleAsync()).Quantity);
        Assert.Equal(100, (await fresh.InventoryItems.SingleAsync()).QuantityOnHand);
    }

    [Fact]
    public void Template_shape_is_bounded_and_snapshots_do_not_follow_later_input_mutation()
    {
        var owner = Guid.NewGuid(); var variant = Guid.NewGuid();
        var snapshots = new List<CartTemplateSnapshot> { new(variant, 2, "Original", "Product", "Variant") };
        var template = new CartTemplate(owner, "  Routine  ", snapshots, DateTimeOffset.UnixEpoch);
        snapshots[0] = snapshots[0] with { Sku = "Changed", Quantity = 9 };
        Assert.Equal("Routine", template.Name); Assert.Equal(("Original", 2), (template.Lines.Single().Sku, template.Lines.Single().Quantity));
        Assert.Throws<DomainException>(() => new CartTemplate(owner, "Empty", [], DateTimeOffset.UnixEpoch));
        Assert.Throws<DomainException>(() => new CartTemplate(owner, "Duplicate", [snapshots[0], snapshots[0]], DateTimeOffset.UnixEpoch));
        Assert.Throws<DomainException>(() => new CartTemplate(owner, "Large", Enumerable.Range(0, 51).Select(_ => new CartTemplateSnapshot(Guid.NewGuid(), 1, "SKU", "P", "V")).ToArray(), DateTimeOffset.UnixEpoch));
        Assert.Throws<DomainException>(() => new CartTemplate(owner, "Bad quantity", [snapshots[0] with { Quantity = 100 }], DateTimeOffset.UnixEpoch));
        Assert.Equal(50, new CartTemplate(owner, "Boundary", Enumerable.Range(0, 50).Select(_ => new CartTemplateSnapshot(Guid.NewGuid(), 99, "SKU", "P", "V")).ToArray(), DateTimeOffset.UnixEpoch).Lines.Count);
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
    private sealed record SeedData(Guid Owner, Guid Variant, string Token);
    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agora-cart-template-" + Guid.NewGuid().ToString("N") + ".db");
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
            var owner = new Customer { Email = "template@example.test", FullName = "Template owner", PasswordHash = "unused-test-hash" };
            var category = new Category { Name = "Category", Slug = "template-category" };
            var product = new Product { Name = "Original product", Slug = "template-product", CategoryId = category.Id, IsActive = true };
            var variant = new ProductVariant { ProductId = product.Id, Sku = "ORIGINAL-SKU", Name = "Original variant", Price = new Money(10) };
            var inventory = new InventoryItem(variant.Id, 100);
            var cart = new Cart { CustomerId = owner.Id }; cart.AddItem(variant.Id, 2);
            db.AddRange(owner, category, product, variant, inventory, cart); await db.SaveChangesAsync();
            return new(owner.Id, variant.Id, cart.Token);
        }
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
}
