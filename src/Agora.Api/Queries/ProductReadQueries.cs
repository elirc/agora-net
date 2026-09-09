using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Queries;

internal static class ProductReadQueries
{
    public static async Task<PagedResult<ProductResponse>> Page(AgoraDbContext db, ProductSearchRequest request, CancellationToken ct)
    {
        var query = ProductCatalogQuery.Apply(db.Products.AsNoTracking(), request);
        var count = await query.CountAsync(ct);
        var products = await WithResponseData(query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)).ToListAsync(ct);
        var ratings = await Ratings(db, products.Select(p => p.Id).ToList(), ct);
        return new(products.Select(p => Response(p, ratings)).ToArray(), request.Page, request.PageSize, count);
    }

    public static IQueryable<Product> WithResponseData(IQueryable<Product> query) => query
        .Include(p => p.Variants).Include(p => p.Images).Include(p => p.TaxCategory)
        .Include(p => p.Tags).ThenInclude(t => t.Tag).AsSplitQuery();

    public static async Task<Dictionary<Guid, (decimal Average, int Count)>> Ratings(
        AgoraDbContext db, List<Guid> productIds, CancellationToken ct)
    {
        var rows = await db.Reviews.AsNoTracking()
            .Where(r => productIds.Contains(r.ProductId) && r.Status == ReviewStatus.Approved)
            .GroupBy(r => r.ProductId)
            .Select(g => new { Id = g.Key, Average = g.Average(r => (double)r.Rating), Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => (decimal.Round((decimal)r.Average, 2), r.Count));
    }

    public static ProductResponse Response(Product product, Dictionary<Guid, (decimal Average, int Count)> ratings) =>
        ratings.TryGetValue(product.Id, out var rating)
            ? ProductResponse.From(product, rating.Average, rating.Count) : ProductResponse.From(product);
}
