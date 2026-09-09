using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

internal static class OperationalHistoryTestData
{
    internal static async Task<Order> Order(AgoraDbContext db, Guid? owner, DateTimeOffset fulfilledAt, int quantity = 5, bool fulfilled = true)
    {
        var variant = await db.ProductVariants.SingleAsync(v => v.Sku == "TEE-BLK-S");
        var order = new Order { Number = "ORD-HISTORY-" + Guid.NewGuid().ToString("N"), CustomerId = owner,
            Email = "historical@example.test", ShippingAddress = CheckoutQuoteApiTests.Address.ToAddress(),
            Currency = "USD", Subtotal = quantity * 20m, DiscountAmount = quantity * 2m,
            TaxAmount = quantity * 1.44m, Total = quantity * 19.44m, CreatedAt = fulfilledAt.AddDays(-2) };
        order.Items.Add(new OrderItem { OrderId = order.Id, ProductVariantId = variant.Id, Sku = variant.Sku,
            ProductName = "Historical tee", VariantName = "Historical variant", UnitPrice = 20, Quantity = quantity, LineTotal = quantity * 20 });
        order.MarkPaid("historical-payment", fulfilledAt.AddDays(-1));
        if (fulfilled) order.MarkFulfilled(fulfilledAt); else order.MarkPartiallyFulfilled();
        db.Orders.Add(order); await db.SaveChangesAsync(); return order;
    }

    internal static ReturnRequest Return(Order order, int quantity, DateTimeOffset now, ReturnStatus status = ReturnStatus.Requested)
    {
        var item = order.Items.Single();
        var request = new ReturnRequest { Number = "RMA-HISTORY-" + Guid.NewGuid().ToString("N"), OrderId = order.Id,
            CustomerId = order.CustomerId, Reason = ReturnReason.Damaged, RefundAmount = quantity * 19.44m, CreatedAt = now };
        request.Items.Add(new ReturnRequestItem { ReturnRequestId = request.Id, OrderItemId = item.Id,
            ProductVariantId = item.ProductVariantId, Sku = item.Sku, Quantity = quantity, RefundAmount = request.RefundAmount });
        if (status == ReturnStatus.Approved) request.Approve("historical-refund", now);
        if (status == ReturnStatus.Rejected) request.Reject("Historical rejection", now);
        if (status == ReturnStatus.Cancelled) request.Cancel(now);
        return request;
    }

    internal static Fulfillment Fulfillment(Order order, DateTimeOffset now)
    {
        var shipment = new Fulfillment { Number = "FUL-HISTORY-" + Guid.NewGuid().ToString("N"), OrderId = order.Id, Carrier = "Manual carrier", CreatedAt = now };
        foreach (var item in order.Items) shipment.Items.Add(new FulfillmentItem { FulfillmentId = shipment.Id, OrderItemId = item.Id,
            ProductVariantId = item.ProductVariantId, Sku = item.Sku, Quantity = item.Quantity });
        return shipment;
    }
}
