using System.Security.Cryptography;
using System.Text.Json;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductInsightsController(AgoraDbContext db) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost("compare")]
    public async Task<ActionResult<ProductComparisonResponse>> Compare(ProductComparisonRequest request, CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking()
            .Where(p => request.ProductIds.Contains(p.Id) && p.IsActive)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.Inventory)
            .AsSplitQuery()
            .ToListAsync(ct);
        var byId = products.ToDictionary(p => p.Id);
        var unusable = request.ProductIds.Where(id => !byId.ContainsKey(id)).ToArray();
        if (unusable.Length != 0)
        {
            var problem = new ProblemDetails { Title = "Every compared product must exist and be active.", Status = 422 };
            problem.Extensions["unusableProductIds"] = unusable;
            return UnprocessableEntity(problem);
        }

        var ratings = await db.Reviews.AsNoTracking()
            .Where(r => request.ProductIds.Contains(r.ProductId) && r.Status == ReviewStatus.Approved)
            .GroupBy(r => r.ProductId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Average = g.Average(r => (double)r.Rating) })
            .ToDictionaryAsync(r => r.Id, ct);

        var result = request.ProductIds.Select(id =>
        {
            var product = byId[id];
            ratings.TryGetValue(id, out var rating);
            return new ComparedProductResponse(product.Id, product.Name, product.Slug,
                CategoryResponse.From(product.Category!),
                product.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(ImageResponse.From).ToList(),
                rating is null ? null : decimal.Round((decimal)rating.Average, 2), rating?.Count ?? 0,
                product.Variants.OrderBy(v => v.Sku, StringComparer.Ordinal).ThenBy(v => v.Id)
                    .Select(v => new ComparisonVariantResponse(v.Id, v.Sku, v.Name,
                        new MoneyDto(v.Price.Amount, v.Price.Currency), v.WeightGrams,
                        v.Options, (v.Inventory?.QuantityAvailable ?? 0) > 0)).ToList());
        }).ToList();
        return Ok(new ProductComparisonResponse(result));
    }

    [HttpGet("{productId:guid}/reviews/summary")]
    public async Task<IActionResult> ReviewSummary(Guid productId, CancellationToken ct)
    {
        // Match the existing public reviews route's product visibility rule.
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct)) return NotFound();
        var counts = await db.Reviews.AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved)
            .GroupBy(r => r.Rating)
            .Select(g => new { Stars = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(r => r.Stars, r => r.Count, ct);
        var buckets = Enumerable.Range(1, 5).Select(stars => new RatingBucketResponse(stars, counts.GetValueOrDefault(stars))).ToArray();
        var count = buckets.Sum(b => b.Count);
        decimal? average = count == 0 ? null : decimal.Round(buckets.Sum(b => (decimal)b.Stars * b.Count) / count, 2);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ReviewSummaryResponse(count, average, buckets), JsonOptions);
        var tag = "\"" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + "\"";
        Response.Headers.ETag = tag;
        Response.Headers.CacheControl = "no-cache";

        // GET uses weak comparison: a matching opaque tag works even with W/.
        if (EntityTagHeaderValue.TryParseList(Request.Headers.IfNoneMatch.OfType<string>().ToArray(), out var validators)
            && validators.Any(v => v.Tag.Value == "*" || v.Tag.Value == tag))
            return StatusCode(StatusCodes.Status304NotModified);

        // Send the very bytes we hashed; a second serialization could change the validator.
        return File(bytes, "application/json; charset=utf-8");
    }
}
