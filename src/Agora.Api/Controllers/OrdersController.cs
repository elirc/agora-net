using Agora.Api.Auth;
using Agora.Api.Contracts;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(
    AgoraDbContext db,
    OrderService orderService,
    FulfillmentService fulfillmentService,
    GuestOrderAccessService orderAccess) : ControllerBase
{
    [HttpGet("{number}", Name = "GetOrderByNumber")]
    public async Task<ActionResult<CustomerOrderResponse>> GetByNumber(string number, CancellationToken ct)
    {
        var order = await orderService.FindAsync(number, ct);
        if (order is null) return NotFound();
        await orderAccess.EnsureCanReadAsync(order, Actor(), ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(CustomerOrderResponse.From(order));
    }

    /// <summary>Ships everything still outstanding as one fulfillment.</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("{number}/fulfill")]
    public async Task<ActionResult<OrderResponse>> Fulfill(
        string number,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)]
        LegacyFulfillRequest? request,
        CancellationToken ct)
    {
        await fulfillmentService.CreateAsync(number, carrier: null, trackingNumber: null,
            lines: null, ct, request?.AssignmentId, User.GetCustomerId()!.Value);
        var order = await orderService.FindAsync(number, ct);
        return Ok(OrderResponse.From(order!));
    }

    /// <summary>Ships specific line quantities with carrier/tracking details.</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("{number}/fulfillments")]
    public async Task<ActionResult<FulfillmentResponse>> CreateFulfillment(
        string number, CreateFulfillmentRequest request, CancellationToken ct)
    {
        var fulfillment = await fulfillmentService.CreateAsync(
            number,
            request.Carrier,
            request.TrackingNumber,
            request.Items?.Select(i => new FulfillmentLineInput(i.OrderItemId, i.Quantity)).ToList(),
            ct,
            request.AssignmentId,
            User.GetCustomerId()!.Value);

        return CreatedAtAction(nameof(ListFulfillments), new { number },
            FulfillmentResponse.From(fulfillment));
    }

    /// <summary>The order's shipments, oldest first.</summary>
    [HttpGet("{number}/fulfillments")]
    public async Task<ActionResult<List<FulfillmentResponse>>> ListFulfillments(
        string number, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Number == number, ct);
        if (order is null)
        {
            return NotFound();
        }
        await orderAccess.EnsureCanReadAsync(order, Actor(), ct);

        var fulfillments = await db.Fulfillments
            .AsNoTracking()
            .Include(f => f.Items)
            .Where(f => f.OrderId == order.Id)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(ct);

        Response.Headers.CacheControl = "private, no-store";
        return Ok(fulfillments.Select(FulfillmentResponse.From).ToList());
    }

    /// <summary>Cancels a pending/paid order; paid orders are refunded and restocked.</summary>
    [HttpPost("{number}/cancel")]
    [Authorize]
    public async Task<ActionResult<CustomerOrderResponse>> Cancel(string number, CancellationToken ct)
    {
        var order = await orderService.FindAsync(number, ct);
        if (order is null) return NotFound();
        await orderAccess.EnsureCanReadAsync(order, new OrderAccessActor(User.GetCustomerId(), User.IsInRole("Admin"), null), ct);
        var cancelled = await orderService.CancelAsync(number, ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(CustomerOrderResponse.From(cancelled));
    }

    /// <summary>Refunds a paid/fulfilled order and restocks its items.</summary>
    [HttpPost("{number}/refund")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<OrderResponse>> Refund(string number, CancellationToken ct) =>
        Ok(OrderResponse.From(await orderService.RefundAsync(number, ct)));

    private OrderAccessActor Actor() => new(User.GetCustomerId(), User.IsInRole("Admin"),
        Request.Headers["X-Agora-Order-Access"].FirstOrDefault());
}
