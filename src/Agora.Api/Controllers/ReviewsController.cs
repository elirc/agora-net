using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api")]
public class ReviewsController(AgoraDbContext db) : ControllerBase
{
    public const int MaxPageSize = 100;

    /// <summary>Approved reviews for a product, newest first.</summary>
    [HttpGet("products/{productId:guid}/reviews")]
    public async Task<ActionResult<PagedResult<ReviewResponse>>> ListForProduct(
        Guid productId,
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

        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
        {
            return NotFound();
        }

        var query = db.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved)
            .OrderByDescending(r => r.CreatedAt)
            .Join(db.Customers,
                r => r.CustomerId,
                c => c.Id,
                (r, c) => new { Review = r, c.FullName, c.Email });

        var totalCount = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return Ok(new PagedResult<ReviewResponse>(
            rows.Select(x => ReviewResponse.From(
                x.Review, ReviewResponse.DisplayName(x.FullName, x.Email))).ToList(),
            page, pageSize, totalCount));
    }

    /// <summary>Submits a verified-purchase review (one per customer per product).</summary>
    [Authorize]
    [HttpPost("products/{productId:guid}/reviews")]
    public async Task<ActionResult<ReviewResponse>> Create(
        Guid productId, CreateReviewRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;

        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
        {
            return NotFound();
        }

        var hasPurchased = await db.Orders.AnyAsync(o =>
            o.CustomerId == customerId
            && (o.Status == OrderStatus.Paid
                || o.Status == OrderStatus.PartiallyFulfilled
                || o.Status == OrderStatus.Fulfilled)
            && o.Items.Any(i => db.ProductVariants.Any(v =>
                v.Id == i.ProductVariantId && v.ProductId == productId)), ct);
        if (!hasPurchased)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Only verified purchasers can review this product.",
            });
        }

        if (await db.Reviews.AnyAsync(r => r.ProductId == productId && r.CustomerId == customerId, ct))
        {
            return Conflict(new ProblemDetails
            {
                Title = "You have already reviewed this product.",
            });
        }

        var review = new Review(productId, customerId, request.Rating, request.Title, request.Body);
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(ListForProduct), new { productId },
            ReviewResponse.From(review, await ReviewerNameAsync(customerId, ct)));
    }

    /// <summary>Edits the caller's review; it returns to moderation.</summary>
    [Authorize]
    [HttpPut("reviews/{id:guid}")]
    public async Task<ActionResult<ReviewResponse>> Update(
        Guid id, CreateReviewRequest request, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var review = await db.Reviews
            .FirstOrDefaultAsync(r => r.Id == id && r.CustomerId == customerId, ct);
        if (review is null)
        {
            return NotFound();
        }

        review.Edit(request.Rating, request.Title, request.Body);
        await db.SaveChangesAsync(ct);

        return Ok(ReviewResponse.From(review, await ReviewerNameAsync(customerId, ct)));
    }

    /// <summary>Deletes the caller's review.</summary>
    [Authorize]
    [HttpDelete("reviews/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var review = await db.Reviews
            .FirstOrDefaultAsync(r => r.Id == id && r.CustomerId == customerId, ct);
        if (review is null)
        {
            return NotFound();
        }

        db.Reviews.Remove(review);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Admin moderation queue, optionally filtered by status (default pending).</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("reviews")]
    public async Task<ActionResult<PagedResult<ReviewResponse>>> Moderate(
        [FromQuery] string? status = "pending",
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

        var query = db.Reviews.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            if (!Enum.TryParse<ReviewStatus>(status, ignoreCase: true, out var parsed))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "status must be 'pending', 'approved', 'rejected', or 'all'.",
                });
            }

            query = query.Where(r => r.Status == parsed);
        }

        var joined = query
            .OrderBy(r => r.CreatedAt)
            .Join(db.Customers,
                r => r.CustomerId,
                c => c.Id,
                (r, c) => new { Review = r, c.FullName, c.Email });

        var totalCount = await joined.CountAsync(ct);
        var rows = await joined.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return Ok(new PagedResult<ReviewResponse>(
            rows.Select(x => ReviewResponse.From(
                x.Review, ReviewResponse.DisplayName(x.FullName, x.Email))).ToList(),
            page, pageSize, totalCount));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("reviews/{id:guid}/approve")]
    public async Task<ActionResult<ReviewResponse>> Approve(Guid id, CancellationToken ct)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (review is null)
        {
            return NotFound();
        }

        review.Approve(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return Ok(ReviewResponse.From(review, await ReviewerNameAsync(review.CustomerId, ct)));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("reviews/{id:guid}/reject")]
    public async Task<ActionResult<ReviewResponse>> Reject(
        Guid id, RejectReviewRequest request, CancellationToken ct)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (review is null)
        {
            return NotFound();
        }

        review.Reject(request.Note, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return Ok(ReviewResponse.From(review, await ReviewerNameAsync(review.CustomerId, ct)));
    }

    /// <summary>Marks an approved review as helpful (one vote per customer).</summary>
    [Authorize]
    [HttpPost("reviews/{id:guid}/helpful")]
    public async Task<ActionResult<ReviewResponse>> VoteHelpful(Guid id, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var review = await db.Reviews
            .FirstOrDefaultAsync(r => r.Id == id && r.Status == ReviewStatus.Approved, ct);
        if (review is null)
        {
            return NotFound();
        }

        if (await db.ReviewVotes.AnyAsync(v => v.ReviewId == id && v.CustomerId == customerId, ct))
        {
            return Conflict(new ProblemDetails
            {
                Title = "You have already marked this review as helpful.",
            });
        }

        db.ReviewVotes.Add(new ReviewVote { ReviewId = id, CustomerId = customerId });
        review.HelpfulCount += 1;
        await db.SaveChangesAsync(ct);

        return Ok(ReviewResponse.From(review, await ReviewerNameAsync(review.CustomerId, ct)));
    }

    /// <summary>Removes the caller's helpful vote.</summary>
    [Authorize]
    [HttpDelete("reviews/{id:guid}/helpful")]
    public async Task<ActionResult<ReviewResponse>> RemoveHelpfulVote(Guid id, CancellationToken ct)
    {
        var customerId = User.GetCustomerId()!.Value;
        var vote = await db.ReviewVotes
            .FirstOrDefaultAsync(v => v.ReviewId == id && v.CustomerId == customerId, ct);
        if (vote is null)
        {
            return NotFound();
        }

        var review = await db.Reviews.FirstAsync(r => r.Id == id, ct);
        db.ReviewVotes.Remove(vote);
        review.HelpfulCount = Math.Max(0, review.HelpfulCount - 1);
        await db.SaveChangesAsync(ct);

        return Ok(ReviewResponse.From(review, await ReviewerNameAsync(review.CustomerId, ct)));
    }

    private async Task<string> ReviewerNameAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new { c.FullName, c.Email })
            .FirstAsync(ct);
        return ReviewResponse.DisplayName(customer.FullName, customer.Email);
    }
}
