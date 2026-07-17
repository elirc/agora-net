using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Unit;

/// <summary>
/// Exercises the SQLite value converters: DateTimeOffset and decimal are stored
/// as longs so that ORDER BY / comparisons translate correctly.
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AgoraDbContext _db;

    public PersistenceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new AgoraDbContext(
            new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Category AddCategory()
    {
        var category = new Category { Name = "Cat", Slug = "cat" };
        _db.Categories.Add(category);
        return category;
    }

    [Fact]
    public async Task DateTimeOffset_RoundTrips_AndOrdersChronologically()
    {
        var category = AddCategory();
        var older = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(-5));
        _db.Products.AddRange(
            new Product { Category = category, Name = "Newer", Slug = "newer", CreatedAt = newer },
            new Product { Category = category, Name = "Older", Slug = "older", CreatedAt = older });
        await _db.SaveChangesAsync();

        var ordered = await _db.Products.OrderBy(p => p.CreatedAt).Select(p => p.Name).ToListAsync();
        var roundTripped = await _db.Products.SingleAsync(p => p.Slug == "newer");

        Assert.Equal(["Older", "Newer"], ordered);
        Assert.Equal(newer.ToUniversalTime(), roundTripped.CreatedAt);
    }

    [Fact]
    public async Task DecimalPrice_RoundTrips_AndSupportsRangeQueries()
    {
        var category = AddCategory();
        var product = new Product { Category = category, Name = "P", Slug = "p" };
        product.Variants.Add(new ProductVariant { Sku = "A", Price = new Money(19.99m) });
        product.Variants.Add(new ProductVariant { Sku = "B", Price = new Money(54.50m) });
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        var cheap = await _db.ProductVariants
            .Where(v => v.Price.Amount <= 20m)
            .Select(v => v.Sku)
            .ToListAsync();
        var priciest = await _db.ProductVariants
            .OrderByDescending(v => v.Price.Amount)
            .FirstAsync();

        Assert.Equal(["A"], cheap);
        Assert.Equal(54.50m, priciest.Price.Amount);
    }

    [Fact]
    public async Task VariantOptions_RoundTripAsJson()
    {
        var category = AddCategory();
        var product = new Product { Category = category, Name = "P", Slug = "p" };
        product.Variants.Add(new ProductVariant
        {
            Sku = "OPT",
            Price = new Money(10m),
            Options = new Dictionary<string, string> { ["Color"] = "Red", ["Size"] = "M" },
        });
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var variant = await _db.ProductVariants.SingleAsync(v => v.Sku == "OPT");

        Assert.Equal("Red", variant.Options["Color"]);
        Assert.Equal("M", variant.Options["Size"]);
    }

    [Fact]
    public async Task Seeder_PopulatesCatalog_AndIsIdempotent()
    {
        await AgoraDbSeeder.SeedAsync(_db);
        await AgoraDbSeeder.SeedAsync(_db);

        Assert.Equal(3, await _db.Categories.CountAsync());
        Assert.True(await _db.Products.CountAsync() >= 8);
        Assert.True(await _db.ProductVariants.CountAsync() >= 10);
        Assert.Equal(await _db.ProductVariants.CountAsync(), await _db.InventoryItems.CountAsync());
        Assert.Equal(3, await _db.DiscountCodes.CountAsync());
    }

    [Fact]
    public async Task DuplicateSku_ViolatesUniqueIndex()
    {
        var category = AddCategory();
        var product = new Product { Category = category, Name = "P", Slug = "p" };
        product.Variants.Add(new ProductVariant { Sku = "DUP", Price = new Money(1m) });
        product.Variants.Add(new ProductVariant { Sku = "DUP", Price = new Money(2m) });
        _db.Products.Add(product);

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }
}
