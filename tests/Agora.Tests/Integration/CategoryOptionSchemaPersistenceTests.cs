using Agora.Domain.Entities;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agora.Tests.Integration;

public class CategoryOptionSchemaPersistenceTests
{
    [Fact]
    public async Task Authoring_and_enforce_publication_serialize_without_a_stale_observe_bypass()
    {
        await using var store = new Store(); Guid categoryId; Guid taxId;
        await using (var setup = store.Context())
        {
            await setup.Database.EnsureCreatedAsync();
            var category = new Category { Name = "Publication race", Slug = "schema-publication-race" };
            var tax = new TaxCategory { Code = "SCHEMA-RACE", Name = "Schema race" };
            setup.AddRange(category, tax, new CategoryOptionSchema(category.Id, CategoryOptionSchemaMode.Observe,
                [new CategoryOptionRule("size", true, ["S", "M", "L"])]));
            await setup.SaveChangesAsync(); categoryId = category.Id; taxId = tax.Id;
        }
        var barrier = new BeginTogether();
        var author = Task.Run(async () =>
        {
            await using var db = store.Context(barrier); await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                var product = Product(categoryId, taxId, "RACE-XL", "XL");
                await new Agora.Infrastructure.Services.CategoryOptionSchemaService(db,
                    NullLogger<Agora.Infrastructure.Services.CategoryOptionSchemaService>.Instance)
                    .ValidateAuthoringAsync(categoryId, [new(product.Variants.Single().Id, "RACE-XL", product.Variants.Single().Options)]);
                db.Products.Add(product); await db.SaveChangesAsync(); await transaction.CommitAsync(); return "created";
            }
            catch (InvalidCategoryOptionsException) { return "enforced"; }
            catch (DbUpdateException) { return "busy"; }
            catch (Microsoft.Data.Sqlite.SqliteException) { return "busy"; }
        });
        var publisher = Task.Run(async () =>
        {
            await using var db = store.Context(barrier); await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                var schema = await db.CategoryOptionSchemas.SingleAsync(); schema.Replace(CategoryOptionSchemaMode.Enforce, schema.ReadRules());
                await db.SaveChangesAsync(); await transaction.CommitAsync(); return "published";
            }
            catch (DbUpdateException) { return "busy"; }
            catch (Microsoft.Data.Sqlite.SqliteException) { return "busy"; }
        });
        var outcomes = await Task.WhenAll(author, publisher);
        if (outcomes[1] == "busy")
        {
            await using var retry = store.Context(); var schema = await retry.CategoryOptionSchemas.SingleAsync();
            schema.Replace(CategoryOptionSchemaMode.Enforce, schema.ReadRules()); await retry.SaveChangesAsync();
        }
        await using var check = store.Context();
        Assert.Equal(CategoryOptionSchemaMode.Enforce, (await check.CategoryOptionSchemas.SingleAsync()).Mode);
        Assert.True(outcomes[0] is "created" or "enforced" or "busy");
        // A created row serialized before publication; every writer that observed Enforce rejected it.
        Assert.Equal(outcomes[0] == "created", await check.Products.AnyAsync(p => p.Slug == "schema-old-product"));
    }

    [Fact]
    public async Task Observe_logs_only_structured_aggregate_counts_and_reason_names()
    {
        await using var store = new Store(); Guid categoryId;
        await using (var setup = store.Context())
        {
            await setup.Database.EnsureCreatedAsync(); var category = new Category { Name = "Observe", Slug = "schema-observe-log" };
            setup.AddRange(category, new CategoryOptionSchema(category.Id, CategoryOptionSchemaMode.Observe,
                [new CategoryOptionRule("size", true, ["M"])])); await setup.SaveChangesAsync(); categoryId = category.Id;
        }
        var logger = new StructuredLogger(); await using var db = store.Context();
        await new Agora.Infrastructure.Services.CategoryOptionSchemaService(db, logger).ValidateAuthoringAsync(categoryId,
            [new(null, "SECRET-SKU", new Dictionary<string, string> { ["size"] = "secret-option-value" }),
             new(null, "SECOND-SECRET", new Dictionary<string, string> { ["material"] = "secret-material" })]);
        Assert.Equal(categoryId, logger.Values["CategoryId"]); Assert.Equal(2, logger.Values["ViolatingVariantCount"]);
        var reasons = Assert.IsAssignableFrom<IReadOnlyDictionary<string, int>>(logger.Values["ReasonCounts"]);
        Assert.Equal(1, reasons["ValueNotAllowed"]); Assert.Equal(1, reasons["UnknownKey"]); Assert.Equal(1, reasons["RequiredKeyMissing"]);
        Assert.DoesNotContain("SECRET-SKU", logger.Rendered); Assert.DoesNotContain("secret-option-value", logger.Rendered);
    }
    [Fact]
    public async Task Upgrade_leaves_old_nonconforming_options_untouched_and_categories_without_rows_are_off()
    {
        await using var store = new Store();
        Guid categoryId; Guid productId;
        await using (var old = store.Context())
        {
            await old.Database.MigrateAsync();
            var category = new Category { Name = "Legacy", Slug = "schema-upgrade-legacy" };
            var tax = new TaxCategory { Code = "SCHEMA-UPGRADE", Name = "Schema upgrade" };
            var product = Product(category.Id, tax.Id, "OLD-XL", "XL");
            old.AddRange(category, tax, product); await old.SaveChangesAsync();
            categoryId = category.Id; productId = product.Id;
            // Seed using the current model, then remove newer schema before testing the upgrade.
            await old.GetService<IMigrator>().MigrateAsync("20260908221636_CategoryTreeRevision");
        }

        await using (var upgraded = store.Context())
        {
            await upgraded.Database.MigrateAsync();
            Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
            Assert.False(await upgraded.CategoryOptionSchemas.AnyAsync(s => s.CategoryId == categoryId));
            var preserved = await upgraded.Products.Include(p => p.Variants).SingleAsync(p => p.Id == productId);
            Assert.Equal("XL", preserved.Variants.Single().Options["size"]);
        }
    }

    [Fact]
    public async Task Revision_is_an_optimistic_token_and_category_deletion_cascades_its_schema()
    {
        await using var store = new Store();
        Guid categoryId;
        await using (var setup = store.Context())
        {
            await setup.Database.EnsureCreatedAsync();
            var category = new Category { Name = "Owned schema", Slug = "schema-owned" }; categoryId = category.Id;
            setup.AddRange(category, new CategoryOptionSchema(category.Id, CategoryOptionSchemaMode.Observe,
                [new CategoryOptionRule("size", true, ["S", "M", "L"])]));
            await setup.SaveChangesAsync();
        }
        await using var first = store.Context(); await using var second = store.Context();
        var firstCopy = await first.CategoryOptionSchemas.SingleAsync(); var secondCopy = await second.CategoryOptionSchemas.SingleAsync();
        firstCopy.Replace(CategoryOptionSchemaMode.Enforce, firstCopy.ReadRules());
        secondCopy.Replace(CategoryOptionSchemaMode.Off, secondCopy.ReadRules());
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using (var delete = store.Context())
        {
            delete.Categories.Remove(await delete.Categories.SingleAsync(c => c.Id == categoryId)); await delete.SaveChangesAsync();
        }
        await using var check = store.Context(); Assert.Empty(await check.CategoryOptionSchemas.ToListAsync());
    }

    private static Product Product(Guid categoryId, Guid taxId, string sku, string size)
    {
        var product = new Product { CategoryId = categoryId, TaxCategoryId = taxId, Name = "Legacy", Slug = "schema-old-product", Description = "" };
        var variant = new ProductVariant { ProductId = product.Id, Sku = sku, Name = sku, Price = new Agora.Domain.Common.Money(10, "USD"), Options = new() { ["size"] = size } };
        variant.Inventory = new InventoryItem(variant.Id, 0); product.Variants.Add(variant); return product;
    }
    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-category-schema-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=30");
            if (interceptor is not null) options.AddInterceptors(interceptor); return new(options.Options);
        }
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
    private sealed class BeginTogether : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource<bool> _both = new(TaskCreationOptions.RunContinuationsAsynchronously); private int _count;
        public override async ValueTask<InterceptionResult<System.Data.Common.DbTransaction>> TransactionStartingAsync(
            System.Data.Common.DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<System.Data.Common.DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _count) <= 2) { if (_count == 2) _both.TrySetResult(true); await _both.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
            return result;
        }
    }
    private sealed class StructuredLogger : ILogger<Agora.Infrastructure.Services.CategoryOptionSchemaService>
    {
        public Dictionary<string, object?> Values { get; } = []; public string Rendered { get; private set; } = "";
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Rendered = formatter(state, exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                foreach (var field in fields) Values[field.Key] = field.Value;
        }
    }
}
