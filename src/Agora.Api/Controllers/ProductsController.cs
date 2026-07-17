using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController(AgoraDbContext db) : ControllerBase
{
    public const int MaxPageSize = 100;

    /// <summary>
    /// Search/filter/paginate the catalog. A product matches a price filter when
    /// any of its variants falls inside the range; price sorting uses the
    /// cheapest variant.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductResponse>>> List(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? categorySlug,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] bool? isActive,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            return BadRequest(new ProblemDetails
            {
                Title = $"page must be >= 1 and pageSize between 1 and {MaxPageSize}.",
            });
        }

        var query = db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(p =>
                EF.Functions.Like(p.Name, term) || EF.Functions.Like(p.Description, term));
        }

        if (categoryId is { } cid)
        {
            query = query.Where(p => p.CategoryId == cid);
        }

        if (!string.IsNullOrWhiteSpace(categorySlug))
        {
            query = query.Where(p => p.Category!.Slug == categorySlug);
        }

        if (minPrice is { } min)
        {
            query = query.Where(p => p.Variants.Any(v => v.Price.Amount >= min));
        }

        if (maxPrice is { } max)
        {
            query = query.Where(p => p.Variants.Any(v => v.Price.Amount <= max));
        }

        if (isActive is { } active)
        {
            query = query.Where(p => p.IsActive == active);
        }

        query = sort?.ToLowerInvariant() switch
        {
            "name" => query.OrderBy(p => p.Name),
            "name_desc" => query.OrderByDescending(p => p.Name),
            "price" => query.OrderBy(p => p.Variants.Min(v => v.Price.Amount)),
            "price_desc" => query.OrderByDescending(p => p.Variants.Min(v => v.Price.Amount)),
            "oldest" => query.OrderBy(p => p.CreatedAt),
            null or "" or "newest" => query.OrderByDescending(p => p.CreatedAt),
            _ => query.OrderByDescending(p => p.CreatedAt),
        };

        var totalCount = await query.CountAsync(ct);
        var products = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new PagedResult<ProductResponse>(
            products.Select(ProductResponse.From).ToList(), page, pageSize, totalCount));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> GetById(Guid id, CancellationToken ct)
    {
        var product = await LoadProduct(p => p.Id == id, ct);
        return product is null ? NotFound() : Ok(ProductResponse.From(product));
    }

    [HttpGet("by-slug/{slug}")]
    public async Task<ActionResult<ProductResponse>> GetBySlug(string slug, CancellationToken ct)
    {
        var product = await LoadProduct(p => p.Slug == slug, ct);
        return product is null ? NotFound() : Ok(ProductResponse.From(product));
    }

    [HttpPost]
    public async Task<ActionResult<ProductResponse>> Create(CreateProductRequest request, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, ct))
        {
            return UnprocessableEntity(new ProblemDetails { Title = "Category does not exist." });
        }

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? SlugGenerator.FromName(request.Name)
            : request.Slug.Trim();
        if (await db.Products.AnyAsync(p => p.Slug == slug, ct))
        {
            return Conflict(new ProblemDetails { Title = $"A product with slug '{slug}' already exists." });
        }

        var skus = request.Variants.Select(v => v.Sku.Trim()).ToList();
        if (skus.Distinct(StringComparer.OrdinalIgnoreCase).Count() != skus.Count)
        {
            return UnprocessableEntity(new ProblemDetails { Title = "Duplicate SKUs in request." });
        }

        if (await db.ProductVariants.AnyAsync(v => skus.Contains(v.Sku), ct))
        {
            return Conflict(new ProblemDetails { Title = "One or more SKUs already exist." });
        }

        var product = new Product
        {
            CategoryId = request.CategoryId,
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description ?? string.Empty,
            IsActive = request.IsActive ?? true,
        };

        foreach (var variantRequest in request.Variants)
        {
            var variant = new ProductVariant
            {
                ProductId = product.Id,
                Sku = variantRequest.Sku.Trim(),
                Name = variantRequest.Name?.Trim() ?? string.Empty,
                Price = new Money(variantRequest.Price, variantRequest.Currency ?? Money.DefaultCurrency),
                Options = variantRequest.Options ?? [],
            };
            variant.Inventory = new InventoryItem(variant.Id, 0);
            product.Variants.Add(variant);
        }

        foreach (var imageRequest in request.Images ?? [])
        {
            product.Images.Add(new ProductImage
            {
                ProductId = product.Id,
                Url = imageRequest.Url,
                AltText = imageRequest.AltText,
                SortOrder = imageRequest.SortOrder,
            });
        }

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ProductResponse.From(product));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> Update(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        var product = await db.Products
            .Include(p => p.Variants)
            .Include(p => p.Images)
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

        product.CategoryId = request.CategoryId;
        product.Name = request.Name.Trim();
        product.Slug = request.Slug.Trim();
        product.Description = request.Description ?? string.Empty;
        product.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);

        return Ok(ProductResponse.From(product));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return NotFound();
        }

        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<Product?> LoadProduct(
        System.Linq.Expressions.Expression<Func<Product, bool>> predicate,
        CancellationToken ct) =>
        db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(predicate, ct);
}
