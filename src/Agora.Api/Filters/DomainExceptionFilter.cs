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
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            InsufficientStockException => (StatusCodes.Status409Conflict, "Insufficient stock"),
            InvalidOrderStateException => (StatusCodes.Status409Conflict, "Invalid order state"),
            InvalidReturnStateException => (StatusCodes.Status409Conflict, "Invalid return state"),
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

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = context.Exception.Message,
        })
        {
            StatusCode = statusCode,
        };
        context.ExceptionHandled = true;
    }
}
