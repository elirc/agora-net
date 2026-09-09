using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record CreateCartTemplateRequest([Required, StringLength(80, MinimumLength = 1)] string Name,
    [Required, StringLength(64, MinimumLength = 1)] string CartToken);
public sealed record ApplyCartTemplateRequest([Required, StringLength(64, MinimumLength = 1)] string TargetCartToken,
    [Required, Range(0, int.MaxValue)] int? ExpectedCartVersion);
public sealed record CartTemplateSummary(Guid Id, string Name, DateTimeOffset CreatedAt, int LineCount);
public sealed record CartTemplateLineResponse(Guid Id, Guid VariantId, int Quantity, string Sku, string ProductName, string VariantName);
public sealed record CartTemplateResponse(Guid Id, string Name, DateTimeOffset CreatedAt, IReadOnlyList<CartTemplateLineResponse> Lines)
{
    public static CartTemplateResponse From(CartTemplate template) => new(template.Id, template.Name, template.CreatedAt,
        template.Lines.OrderBy(l => l.Sku, StringComparer.Ordinal).ThenBy(l => l.Id).Select(l =>
            new CartTemplateLineResponse(l.Id, l.VariantId, l.Quantity, l.Sku, l.ProductName, l.VariantName)).ToArray());
}
