using Agora.Api.Contracts;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Agora.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(OrderService orderService) : ControllerBase
{
    [HttpGet("{number}", Name = "GetOrderByNumber")]
    public async Task<ActionResult<OrderResponse>> GetByNumber(string number, CancellationToken ct)
    {
        var order = await orderService.FindAsync(number, ct);
        return order is null ? NotFound() : Ok(OrderResponse.From(order));
    }

    [HttpPost("{number}/fulfill")]
    public async Task<ActionResult<OrderResponse>> Fulfill(string number, CancellationToken ct) =>
        Ok(OrderResponse.From(await orderService.FulfillAsync(number, ct)));

    /// <summary>Cancels a pending/paid order; paid orders are refunded and restocked.</summary>
    [HttpPost("{number}/cancel")]
    public async Task<ActionResult<OrderResponse>> Cancel(string number, CancellationToken ct) =>
        Ok(OrderResponse.From(await orderService.CancelAsync(number, ct)));

    /// <summary>Refunds a paid/fulfilled order and restocks its items.</summary>
    [HttpPost("{number}/refund")]
    public async Task<ActionResult<OrderResponse>> Refund(string number, CancellationToken ct) =>
        Ok(OrderResponse.From(await orderService.RefundAsync(number, ct)));
}
