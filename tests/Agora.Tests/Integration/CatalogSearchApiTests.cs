using System.Net;
using System.Net.Http.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

/// <summary>Behavioral regressions over real SQLite, including adversarial variant combinations.</summary>
public class CatalogSearchApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PriceRange_NoSingleVariantInsideRange_ExcludesProduct()
    {
        var category = await ArrangeProducts(
            ("Outside", [(10m, "USD", 5, 0), (100m, "USD", 5, 0)]),
            ("Inside", [(30m, "USD", 5, 0)]));

        var result = await Search($"categoryId={category}&minPrice=20&maxPrice=40");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Inside", Assert.Single(result.Items).Name);
    }

    [Fact]
    public async Task StockAndPrice_MustMatchTheSameVariant()
    {
        var category = await ArrangeProducts(
            ("Cheap sold out", [(25m, "USD", 0, 0), (90m, "USD", 4, 0)]),
            ("Affordable available", [(25m, "USD", 4, 1)]),
            ("Fully reserved", [(25m, "USD", 4, 4)]));

        var result = await Search($"categoryId={category}&maxPrice=30&inStock=true");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Affordable available", Assert.Single(result.Items).Name);
        var unavailable = await Search($"categoryId={category}&maxPrice=30&inStock=false");
        Assert.Equal(2, unavailable.TotalCount);
        Assert.Contains(unavailable.Items, p => p.Name == "Fully reserved");
        Assert.Contains(unavailable.Items, p => p.Name == "Cheap sold out");
    }

    [Fact]
    public async Task MissingInventory_IsUnavailable()
    {
        var category = await ArrangeProducts(("Missing", [(25m, "USD", 0, 0)]));
        await factory.WithDbAsync(async db =>
        {
            var inventory = await db.InventoryItems.SingleAsync(i => i.ProductVariant!.Product!.CategoryId == category);
            db.InventoryItems.Remove(inventory);
            await db.SaveChangesAsync();
        });

        Assert.Empty((await Search($"categoryId={category}&inStock=true")).Items);
        Assert.Single((await Search($"categoryId={category}&inStock=false")).Items);
    }

    [Fact]
    public async Task CurrencyAndPrice_MustMatchTheSameVariant_AndAcceptLowercase()
    {
        var category = await ArrangeProducts(
            ("Wrong currency", [(25m, "EUR", 5, 0), (90m, "USD", 5, 0)]),
            ("Dollars", [(25m, "USD", 5, 0)]));

        var result = await Search($"categoryId={category}&maxPrice=30&currency=usd");

        Assert.Equal("Dollars", Assert.Single(result.Items).Name);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task Search_LikeMetacharacters_AreLiteral(string term)
    {
        var category = await ArrangeProducts(
            ($"Literal {term}", [(10m, "USD", 1, 0)]),
            ("Ordinary", [(10m, "USD", 1, 0)]));

        var result = await Search($"categoryId={category}&search={Uri.EscapeDataString(term)}");

        Assert.Equal($"Literal {term}", Assert.Single(result.Items).Name);
    }

    [Theory]
    [InlineData("minPrice=-1")]
    [InlineData("maxPrice=-1")]
    [InlineData("minPrice=19.991")]
    [InlineData("maxPrice=19.999")]
    [InlineData("maxPrice=79228162514264337593543950335")]
    [InlineData("minPrice=1000001")]
    [InlineData("minPrice=40&maxPrice=20")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("currency=US")]
    [InlineData("currency=123")]
    [InlineData("inStock=maybe")]
    public async Task InvalidQuery_ReturnsValidationProblem(string query)
    {
        var response = await _client.GetAsync($"/api/products?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("name_desc")]
    [InlineData("price")]
    [InlineData("price_desc")]
    [InlineData("newest")]
    [InlineData("oldest")]
    [InlineData("unknown")]
    public async Task EqualSortKeys_PagesFollowUniqueIdOrder(string sort)
    {
        var category = await ArrangeProducts(
            ("Tie", [(10m, "USD", 1, 0)]),
            ("Tie", [(10m, "USD", 1, 0)]),
            ("Tie", [(10m, "USD", 1, 0)]));
        List<Guid> expected = [];
        await factory.WithDbAsync(async db => expected = await db.Products
            .Where(p => p.CategoryId == category).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync());

        var actual = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await Search($"categoryId={category}&sort={sort}&pageSize=1&page={page}");
            Assert.Equal(3, result.TotalCount);
            actual.Add(Assert.Single(result.Items).Id);
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task InclusiveBounds_KeepAllResponseVariants_AndEmptyPagesKeepCount()
    {
        var category = await ArrangeProducts(("Boundary", [(0m, "USD", 1, 0), (100m, "USD", 1, 0)]));
        var result = await Search($"categoryId={category}&minPrice=0&maxPrice=0");
        Assert.Equal(2, Assert.Single(result.Items).Variants.Count);

        var emptyPage = await Search($"categoryId={category}&page=2&pageSize=1");
        Assert.Empty(emptyPage.Items);
        Assert.Equal(1, emptyPage.TotalCount);
    }

    private async Task<PagedResult<ProductResponse>> Search(string query)
    {
        var response = await _client.GetAsync($"/api/products?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>())!;
    }

    // Every scenario owns a category so class-fixture state cannot change its results.
    private async Task<Guid> ArrangeProducts(params (string Name,
        (decimal Price, string Currency, int OnHand, int Reserved)[] Variants)[] specifications)
    {
        var category = new Category { Name = "Search scenario", Slug = Guid.NewGuid().ToString("N") };
        await factory.WithDbAsync(async db =>
        {
            db.Categories.Add(category);
            foreach (var spec in specifications)
            {
                var product = new Product
                {
                    CategoryId = category.Id, Name = spec.Name, Slug = Guid.NewGuid().ToString("N"),
                    CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                };
                foreach (var v in spec.Variants)
                {
                    var variant = new ProductVariant
                    {
                        ProductId = product.Id, Sku = Guid.NewGuid().ToString("N"),
                        Price = new Money(v.Price, v.Currency),
                    };
                    variant.Inventory = new InventoryItem(variant.Id, v.OnHand);
                    if (v.Reserved > 0) variant.Inventory.Reserve(v.Reserved);
                    product.Variants.Add(variant);
                }
                db.Products.Add(product);
            }
            await db.SaveChangesAsync();
        });
        return category.Id;
    }
}
