using Agora.Api.Contracts;
using Agora.Api.Queries;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController(AgoraDbContext db, CategoryOptionSchemaService optionSchemas,
    ProductDraftService productDrafts, CatalogMutationService catalogFeed, TimeProvider clock) : ControllerBase
{
    public const int MaxPageSize = ProductSearchRequest.MaxPageSize;

    /// <summary>Search the catalog; all variant filters must match one variant.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductResponse>>> List(
        [FromQuery] ProductSearchRequest request,
        CancellationToken ct = default)
    {
        return Ok(await ProductReadQueries.Page(db, request, ct));
    }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> GetById(Guid id, CancellationToken ct)
    {
        var product = await LoadProduct(p => p.Id == id, ct);
        return product is null
            ? NotFound()
            : Ok(ToResponse(product, await LoadRatings([product.Id], ct)));
    }

    [HttpGet("by-slug/{slug}")]
    public async Task<ActionResult<ProductResponse>> GetBySlug(string slug, CancellationToken ct)
    {
        var product = await LoadProduct(p => p.Slug == slug, ct);
        return product is null
            ? NotFound()
            : Ok(ToResponse(product, await LoadRatings([product.Id], ct)));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<ProductResponse>> Create(CreateProductRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var validation = await productDrafts.ValidateAndBuildAsync(request.ToDraft(), ct);
        if (validation.Errors.Count != 0)
        {
            var error = validation.Errors[0];
            return StatusCode(error.Status, new ProblemDetails { Status = error.Status, Title = error.Message });
        }
        var product = validation.Product!;
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        await db.Entry(product).Reference(p => p.TaxCategory).LoadAsync(ct);
        await catalogFeed.StageUpsertAsync(product, clock.GetUtcNow(), ct);
        await transaction.CommitAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ProductResponse.From(product));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<ProductResponse>> Update(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var product = await ProductReadQueries.WithResponseData(db.Products)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return NotFound();
        }

        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, ct))
        {
            return UnprocessableEntity(new ProblemDetails { Title = "Category does not exist." });
        }

        if (await db.Products.AnyAsync(p => p.Slug == request.Slug && p.Id != id, ct))
        {
            return Conflict(new ProblemDetails { Title = $"A product with slug '{request.Slug}' already exists." });
        }

        var (taxCategoryId, taxCategoryError) =
            await ResolveTaxCategoryAsync(request.TaxCategoryCode, ct);
        if (taxCategoryError is not null)
        {
            return taxCategoryError;
        }

        if (product.CategoryId != request.CategoryId)
            await optionSchemas.ValidateAuthoringAsync(request.CategoryId,
                product.Variants.Select(v => new VariantOptionCandidate(v.Id, v.Sku, v.Options)).ToArray(), ct);
        product.CategoryId = request.CategoryId;
        product.Name = request.Name.Trim();
        product.Slug = request.Slug.Trim();
        product.Description = request.Description ?? string.Empty;
        product.IsActive = request.IsActive;
        product.TaxCategoryId = taxCategoryId;
        await db.SaveChangesAsync(ct);
        await db.Entry(product).Reference(p => p.TaxCategory).LoadAsync(ct);
        await catalogFeed.StageUpsertAsync(product, clock.GetUtcNow(), ct);
        await transaction.CommitAsync(ct);

        return Ok(ProductResponse.From(product));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return NotFound();
        }

        // Cascading variant deletion also changes wishlist membership.
        var affectedWishlists = await db.Wishlists
            .Where(w => w.Items.Any(i => i.ProductVariant!.ProductId == id)).ToListAsync(ct);
        foreach (var wishlist in affectedWishlists) wishlist.MembershipChanged();
        var affectedCollections = await db.ProductCollections
            .Where(c => c.Items.Any(i => i.ProductId == id)).ToListAsync(ct);
        foreach (var collection in affectedCollections) collection.MembershipChanged();
        var affectedCarts = await db.Carts.Where(c => c.Items.Any(i => i.ProductVariant!.ProductId == id)).ToListAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var cart in affectedCarts) cart.MembershipChanged(now);
        await catalogFeed.StageDeleteAsync(product, now, ct);
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    /// <summary>Maps an optional tax category code to its id; error result when unknown.</summary>
    private async Task<(Guid? Id, ObjectResult? Error)> ResolveTaxCategoryAsync(
        string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return (null, null);
        }

        var normalized = code.Trim().ToLowerInvariant();
        var category = await db.TaxCategories.FirstOrDefaultAsync(c => c.Code == normalized, ct);
        return category is null
            ? (null, UnprocessableEntity(new ProblemDetails
            {
                Title = $"Tax category '{normalized}' does not exist.",
            }))
            : (category.Id, null);
    }

    /// <summary>Approved-review aggregates (average rating, count) for a set of products.</summary>
    private Task<Dictionary<Guid, (decimal Average, int Count)>> LoadRatings(
        List<Guid> productIds, CancellationToken ct) => ProductReadQueries.Ratings(db, productIds, ct);

    private static ProductResponse ToResponse(
        Product product, Dictionary<Guid, (decimal Average, int Count)> ratings) =>
        ProductReadQueries.Response(product, ratings);

    private Task<Product?> LoadProduct(
        System.Linq.Expressions.Expression<Func<Product, bool>> predicate,
        CancellationToken ct) =>
        ProductReadQueries.WithResponseData(db.Products.AsNoTracking())
            .FirstOrDefaultAsync(predicate, ct);
}
