using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Agora.Tests.Integration;

public class ProductInsightsApiTests(AgoraApiFactory factory) : IClassFixture<AgoraApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static string Key() => Guid.NewGuid().ToString("N");

    private async Task<Product[]> Catalog()
    {
        var category = new Category { Name = "Comparison", Slug = Key() };
        var products = Enumerable.Range(0, 4).Select(i => new Product
        {
            CategoryId = category.Id, Name = "Product " + i, Slug = Key(),
        }).ToArray();
        foreach (var (product, i) in products.Select((p, i) => (p, i)))
        {
            var variant = new ProductVariant
            {
                ProductId = product.Id, Sku = Key(), Name = "Choice", WeightGrams = 500,
                Price = new Money(12.50m, i == 0 ? "USD" : "EUR"),
                Options = new Dictionary<string, string> { ["Size"] = "Large" },
            };
            variant.Inventory = new InventoryItem(variant.Id, i);
            product.Variants.Add(variant);
        }
        await factory.WithDbAsync(async db =>
        {
            db.Categories.Add(category);
            db.Products.AddRange(products);
            await db.SaveChangesAsync();
        });
        return products;
    }

    private async Task<Review> Review(Guid productId, int stars, ReviewStatus status)
    {
        var customer = new Customer { Email = Key() + "@insights.test", FullName = "Reviewer" };
        var review = new Review(productId, customer.Id, stars, "Title", "Useful review");
        if (status == ReviewStatus.Approved) review.Approve(DateTimeOffset.UtcNow);
        if (status == ReviewStatus.Rejected) review.Reject("Moderation", DateTimeOffset.UtcNow);
        await factory.WithDbAsync(async db =>
        {
            db.Customers.Add(customer);
            db.Reviews.Add(review);
            await db.SaveChangesAsync();
        });
        return review;
    }

    private async Task<JsonElement> Compare(params Guid[] ids)
    {
        var response = await _client.PostAsJsonAsync("/api/products/compare", new { productIds = ids });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Comparison_preserves_input_order_currency_choices_and_approved_aggregates()
    {
        var products = await Catalog();
        await Review(products[0].Id, 5, ReviewStatus.Approved);
        await Review(products[0].Id, 3, ReviewStatus.Approved);
        await Review(products[0].Id, 1, ReviewStatus.Pending);
        var result = (await Compare(products[1].Id, products[0].Id)).GetProperty("products");
        Assert.Equal(products[1].Id, result[0].GetProperty("id").GetGuid());
        Assert.Equal(products[0].Id, result[1].GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, result[0].GetProperty("averageRating").ValueKind);
        Assert.Equal(4m, result[1].GetProperty("averageRating").GetDecimal());
        Assert.Equal(2, result[1].GetProperty("reviewCount").GetInt32());
        Assert.Empty(result[0].GetProperty("images").EnumerateArray());
        Assert.Equal("EUR", result[0].GetProperty("variants")[0].GetProperty("price").GetProperty("currency").GetString());
        var unavailable = result[1].GetProperty("variants")[0];
        Assert.Equal("USD", unavailable.GetProperty("price").GetProperty("currency").GetString());
        Assert.Equal(500, unavailable.GetProperty("weightGrams").GetInt32());
        Assert.Equal("Large", unavailable.GetProperty("options").GetProperty("Size").GetString());
        Assert.False(unavailable.GetProperty("inStock").GetBoolean());
        Assert.True(result[0].GetProperty("variants")[0].GetProperty("inStock").GetBoolean());
    }

    [Fact]
    public async Task Comparison_rejects_invalid_requests_and_reports_every_unusable_id()
    {
        var products = await Catalog();
        foreach (var ids in new[] { Array.Empty<Guid>(), new[] { products[0].Id },
            new[] { products[0].Id, products[0].Id }, new[] { Guid.Empty, products[0].Id },
            Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray() })
            Assert.Equal(HttpStatusCode.BadRequest,
                (await _client.PostAsJsonAsync("/api/products/compare", new { productIds = ids })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/products/compare", new { productIds = (Guid[]?)null })).StatusCode);
        await factory.WithDbAsync(async db =>
        {
            (await db.Products.SingleAsync(p => p.Id == products[1].Id)).IsActive = false;
            await db.SaveChangesAsync();
        });
        var missing = Guid.NewGuid();
        var response = await _client.PostAsJsonAsync("/api/products/compare",
            new { productIds = new[] { products[0].Id, missing, products[1].Id } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { missing, products[1].Id },
            body.GetProperty("unusableProductIds").EnumerateArray().Select(x => x.GetGuid()));
        Assert.False(body.TryGetProperty("products", out _));
    }

    [Fact]
    public async Task Comparison_executes_a_fixed_number_of_selects_for_two_and_four_products()
    {
        var products = await Catalog();
        using var commands = new CommandLog();
        factory.Services.GetRequiredService<ILoggerFactory>().AddProvider(commands);
        await Compare(products[0].Id, products[1].Id);
        var two = commands.Statements.ToArray();
        commands.Statements.Clear();
        await Compare(products.Select(p => p.Id).ToArray());
        var four = commands.Statements.ToArray();
        Assert.InRange(two.Length, 1, 5);
        Assert.Equal(two.Length, four.Length);
        Assert.All(two.Concat(four), sql => Assert.DoesNotContain("INSERT INTO", sql));
        Assert.All(two.Concat(four), sql => Assert.DoesNotContain("UPDATE ", sql));
        Assert.All(two.Concat(four), sql => Assert.DoesNotContain("DELETE FROM", sql));
    }

    [Fact]
    public async Task Summary_hashes_response_bytes_and_supports_conditional_gets_and_moderation()
    {
        var product = (await Catalog())[0];
        await Review(product.Id, 5, ReviewStatus.Approved);
        await Review(product.Id, 5, ReviewStatus.Approved);
        var edited = await Review(product.Id, 3, ReviewStatus.Approved);
        var pending = await Review(product.Id, 1, ReviewStatus.Pending);
        await Review(product.Id, 2, ReviewStatus.Rejected);
        var path = $"/api/products/{product.Id}/reviews/summary";
        var response = await _client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var tag = response.Headers.ETag!.ToString();
        Assert.Equal("\"" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + "\"", tag);
        var body = JsonSerializer.Deserialize<JsonElement>(bytes);
        Assert.Equal(3, body.GetProperty("totalCount").GetInt64());
        Assert.Equal(4.33m, body.GetProperty("averageRating").GetDecimal());
        Assert.Equal(new long[] { 0, 0, 1, 0, 2 }, body.GetProperty("buckets").EnumerateArray().Select(b => b.GetProperty("count").GetInt64()));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, body.GetProperty("buckets").EnumerateArray().Select(b => b.GetProperty("stars").GetInt32()));
        foreach (var condition in new[] { tag, "W/" + tag, "\"different\", " + tag, "*" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("If-None-Match", condition);
            var unchanged = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
            Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());
            Assert.Equal(tag, unchanged.Headers.ETag!.ToString());
        }
        using var nonmatching = new HttpRequestMessage(HttpMethod.Get, path);
        nonmatching.Headers.TryAddWithoutValidation("If-None-Match", "\"different\"");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(nonmatching)).StatusCode);
        await factory.WithDbAsync(async db =>
        {
            (await db.Reviews.SingleAsync(r => r.Id == edited.Id)).Edit(4, "Changed", "Pending again");
            await db.SaveChangesAsync();
        });
        var afterEdit = await _client.GetAsync(path);
        Assert.NotEqual(tag, afterEdit.Headers.ETag!.ToString());
        Assert.Equal(2, (await afterEdit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt64());
        await factory.WithDbAsync(async db =>
        {
            (await db.Reviews.SingleAsync(r => r.Id == pending.Id)).Approve(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });
        var afterApproval = await _client.GetAsync(path);
        Assert.NotEqual(afterEdit.Headers.ETag, afterApproval.Headers.ETag);
        Assert.Equal(3, (await afterApproval.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task Empty_summary_is_zero_filled_and_missing_product_is_not_a_cached_representation()
    {
        var product = (await Catalog())[0];
        var response = await _client.GetAsync($"/api/products/{product.Id}/reviews/summary");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("totalCount").GetInt64());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("averageRating").ValueKind);
        Assert.Equal(5, body.GetProperty("buckets").GetArrayLength());
        Assert.All(body.GetProperty("buckets").EnumerateArray(), b => Assert.Equal(0, b.GetProperty("count").GetInt64()));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{Guid.NewGuid()}/reviews/summary");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(request)).StatusCode);
    }

    private sealed class CommandLog : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Statements { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (id.Id == 20101) Statements.Enqueue(formatter(state, exception));
        }
        public void Dispose() { }
    }
}
