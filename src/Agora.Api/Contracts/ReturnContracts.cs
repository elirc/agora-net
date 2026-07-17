using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record ReturnLineDto(
    [Required] Guid OrderItemId,
    [Range(1, 999)] int Quantity);

public sealed record CreateReturnRequestDto(
    [EmailAddress, MaxLength(320)] string? Email,
    [Required, MaxLength(32)] string Reason,
    [MaxLength(500)] string? Comment,
    [Required, MinLength(1)] List<ReturnLineDto> Items);

public sealed record CancelReturnRequestDto(
    [EmailAddress, MaxLength(320)] string? Email);

public sealed record RejectReturnRequestDto(
    [MaxLength(500)] string? Note);

public sealed record ReturnItemResponse(
    Guid OrderItemId,
    Guid ProductVariantId,
    string Sku,
    int Quantity,
    decimal RefundAmount)
{
    public static ReturnItemResponse From(ReturnRequestItem item) => new(
        item.OrderItemId,
        item.ProductVariantId,
        item.Sku,
        item.Quantity,
        item.RefundAmount);
}

public sealed record ReturnResponse(
    string Number,
    string OrderNumber,
    string Status,
    string Reason,
    string Comment,
    string? RejectionNote,
    decimal RefundAmount,
    string Currency,
    string? RefundTransactionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    IReadOnlyList<ReturnItemResponse> Items)
{
    /// <summary>Maps a return whose order and items are loaded.</summary>
    public static ReturnResponse From(ReturnRequest request) => new(
        request.Number,
        request.Order?.Number ?? string.Empty,
        request.Status.ToString(),
        request.Reason.ToString(),
        request.Comment,
        request.RejectionNote,
        request.RefundAmount,
        request.Currency,
        request.RefundTransactionId,
        request.CreatedAt,
        request.ProcessedAt,
        request.Items.Select(ReturnItemResponse.From).ToList());
}
