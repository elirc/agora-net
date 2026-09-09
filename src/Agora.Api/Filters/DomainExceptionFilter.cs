using Agora.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Agora.Api.Filters;

/// <summary>Maps domain exceptions to RFC 7807 ProblemDetails responses.</summary>
public sealed class DomainExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (statusCode, title) = context.Exception switch
        {
            // A competing write won. A caller must assess earlier side effects
            // before retrying workflows such as checkout.
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "Concurrency conflict"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            Agora.Domain.Entities.CatalogSnapshotTooLargeException => (StatusCodes.Status422UnprocessableEntity, "Catalog payload too large"),
            Agora.Domain.Entities.CatalogCursorException => (StatusCodes.Status400BadRequest, "Invalid catalog cursor"),
            Agora.Domain.Services.InvalidCategoryTreeException => (StatusCodes.Status422UnprocessableEntity, "Invalid category tree"),
            Agora.Domain.Services.CategoryTreeConflictException => (StatusCodes.Status409Conflict, "Category tree conflict"),
            Agora.Domain.Entities.CategoryOptionSchemaStateException => (StatusCodes.Status409Conflict, "Option schema cannot be interpreted"),
            Agora.Domain.Entities.ProcurementConflictException => (StatusCodes.Status409Conflict, "Purchase order conflict"),
            Agora.Domain.Entities.InvalidProcurementException => (StatusCodes.Status422UnprocessableEntity, "Invalid purchase order or receipt"),
            Agora.Domain.Entities.InventoryCountConflictException => (StatusCodes.Status409Conflict, "Inventory count conflict"),
            Agora.Domain.Entities.InvalidInventoryCountException => (StatusCodes.Status422UnprocessableEntity, "Invalid inventory count"),
            Agora.Domain.Entities.InvalidQuantityPricingException => (StatusCodes.Status422UnprocessableEntity, "Invalid quantity pricing"),
            Agora.Domain.Entities.WarehouseCoordinationConflictException => (StatusCodes.Status409Conflict, "Warehouse coordination conflict"),
            Agora.Domain.Entities.InvalidWebhookReplayException => (StatusCodes.Status422UnprocessableEntity, "Invalid webhook replay"),
            Agora.Domain.Services.InvalidCategoryOptionsException => (StatusCodes.Status422UnprocessableEntity, "Invalid category options"),
            InsufficientStockException => (StatusCodes.Status409Conflict, "Insufficient stock"),
            Agora.Domain.Entities.InventoryAdjustmentConflictException => (StatusCodes.Status409Conflict, "Inventory adjustment conflict"),
            Agora.Domain.Entities.InvalidInventoryAdjustmentException => (StatusCodes.Status422UnprocessableEntity, "Invalid inventory adjustment"),
            InvalidOrderStateException => (StatusCodes.Status409Conflict, "Invalid order state"),
            InvalidReturnStateException => (StatusCodes.Status409Conflict, "Invalid return state"),
            InvalidFulfillmentException => (StatusCodes.Status422UnprocessableEntity, "Invalid fulfillment"),
            InvalidWebhookDeliveryException => (StatusCodes.Status409Conflict, "Invalid webhook delivery"),
            InvalidReturnRequestException => (StatusCodes.Status422UnprocessableEntity, "Invalid return request"),
            InvalidDiscountException => (StatusCodes.Status422UnprocessableEntity, "Invalid discount"),
            InvalidShippingMethodException => (StatusCodes.Status422UnprocessableEntity, "Invalid shipping method"),
            InvalidGiftCardException => (StatusCodes.Status422UnprocessableEntity, "Invalid gift card"),
            PaymentFailedException => (StatusCodes.Status402PaymentRequired, "Payment failed"),
            DomainException => (StatusCodes.Status400BadRequest, "Domain rule violation"),
            _ => (0, string.Empty),
        };

        if (statusCode == 0)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = context.Exception.Message,
        };
        if (context.Exception is Agora.Domain.Services.InvalidCategoryTreeException treeError)
            problem.Extensions["issues"] = treeError.Issues;
        if (context.Exception is Agora.Domain.Services.InvalidCategoryOptionsException optionError)
            problem.Extensions["variants"] = optionError.Violations;
        context.Result = new ObjectResult(problem)
        {
            StatusCode = statusCode,
        };
        context.ExceptionHandled = true;
    }
}
