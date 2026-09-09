using System.ComponentModel.DataAnnotations;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/products/{productId:guid}")]
public class CatalogEditingController(AgoraDbContext db, CategoryOptionSchemaService optionSchemas,
    CatalogMutationService catalogFeed, TimeProvider clock) : ControllerBase
{
    [HttpGet("variants/{variantId:guid}")]
    public async Task<ActionResult<AdminVariantResponse>> GetVariant(Guid productId, Guid variantId, CancellationToken ct)
    {
        var variant = await db.ProductVariants.AsNoTracking().SingleOrDefaultAsync(v => v.Id == variantId && v.ProductId == productId, ct);
        return variant is null ? NotFound() : Ok(AdminVariantResponse.From(variant));
    }

    [HttpPut("variants/{variantId:guid}")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<AdminVariantResponse>> EditVariant(Guid productId, Guid variantId, EditVariantRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var variant = await db.ProductVariants.SingleOrDefaultAsync(v => v.Id == variantId && v.ProductId == productId, ct);
        if (variant is null) return NotFound();
        if (variant.Version != request.ExpectedVersion) return Conflict(new ProblemDetails { Title = "Variant has changed. Reload before editing." });
        var options = VariantOptionRules.Normalize(request.Options);
        if (!CategoryOptionSchemaRules.SameOptions(variant.Options, options))
        {
            var categoryId = await db.Products.Where(p => p.Id == productId).Select(p => p.CategoryId).SingleAsync(ct);
            await optionSchemas.ValidateAuthoringAsync(categoryId, [new(variant.Id, variant.Sku, options)], ct);
        }
        variant.Edit(request.Name, request.Price!.Value, request.WeightGrams!.Value, options);
        await db.SaveChangesAsync(ct);
        var product = await db.Products.SingleAsync(p => p.Id == productId, ct);
        await catalogFeed.StageUpsertAsync(product, clock.GetUtcNow(), ct);
        await transaction.CommitAsync(ct);
        return Ok(AdminVariantResponse.From(variant));
    }

    [HttpGet("images")]
    public async Task<ActionResult<GalleryResponse>> GetGallery(Guid productId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().Include(p => p.Images).SingleOrDefaultAsync(p => p.Id == productId, ct);
        return product is null ? NotFound() : Ok(GalleryResponse.From(product));
    }

    [HttpPost("images")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<GalleryResponse>> AddImage(Guid productId, AddGalleryImageRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var product = await db.Products.Include(p => p.Images).SingleOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return NotFound();
        if (product.ImageRevision != request.ExpectedVersion) return Conflict(new ProblemDetails { Title = "Gallery has changed. Reload before editing." });
        if (product.Images.Count >= 10) return UnprocessableEntity(new ProblemDetails { Title = "Remove an image before adding to a gallery of ten or more images." });
        var image = product.AddGalleryImage(request.Url, request.AltText);
        db.ProductImages.Add(image);
        await db.SaveChangesAsync(ct);
        await catalogFeed.StageUpsertAsync(product, clock.GetUtcNow(), ct);
        await transaction.CommitAsync(ct);
        return CreatedAtAction(nameof(GetGallery), new { productId }, GalleryResponse.From(product));
    }

    [HttpPut("images/order")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<GalleryResponse>> Reorder(Guid productId, ReorderGalleryRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var product = await db.Products.Include(p => p.Images).SingleOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return NotFound();
        if (product.ImageRevision != request.ExpectedVersion) return Conflict(new ProblemDetails { Title = "Gallery has changed. Reload before editing." });
        if (request.ImageIds.Distinct().Count() != request.ImageIds.Count || !product.Images.Select(i => i.Id).ToHashSet().SetEquals(request.ImageIds))
            return UnprocessableEntity(new ProblemDetails { Title = "Supply every current image ID exactly once." });
        product.ReplaceImageOrder(request.ImageIds);
        await db.SaveChangesAsync(ct);
        await catalogFeed.StageUpsertAsync(product, clock.GetUtcNow(), ct);
        await transaction.CommitAsync(ct);
        return Ok(GalleryResponse.From(product));
    }

    [HttpDelete("images/{imageId:guid}")]
    [Agora.Api.Filters.LocalSqliteWrite]
    public async Task<ActionResult<GalleryResponse>> RemoveImage(Guid productId, Guid imageId,
        [FromQuery, Required, Range(0, long.MaxValue)] long? expectedVersion, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var product = await db.Products.Include(p => p.Images).SingleOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null || product.Images.All(i => i.Id != imageId)) return NotFound();
        if (product.ImageRevision != expectedVersion) return Conflict(new ProblemDetails { Title = "Gallery has changed. Reload before editing." });
        product.RemoveGalleryImage(imageId);
        await db.SaveChangesAsync(ct);
        await catalogFeed.StageUpsertAsync(product, clock.GetUtcNow(), ct);
        await transaction.CommitAsync(ct);
        return Ok(GalleryResponse.From(product));
    }
}
