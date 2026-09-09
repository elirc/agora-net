using System.ComponentModel.DataAnnotations;
using Agora.Domain.Entities;

namespace Agora.Api.Contracts;

public sealed record InventoryAdjustmentLineRequest(Guid VariantId,
    [Range(-1_000_000, 1_000_000)] int Delta, [Required, Range(0, int.MaxValue)] int? ExpectedVersion);
public sealed record InventoryAdjustmentRequest(Guid OperationId, [Required] string Reason,
    [Required, MinLength(1), MaxLength(50)] List<InventoryAdjustmentLineRequest> Lines) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Lines is not null && Lines.Any(l => l is null))
            yield return new ValidationResult("Adjustment lines cannot be null.", [nameof(Lines)]);
    }
}
public sealed record InventoryAdjustmentLineResponse(Guid VariantId, string Sku, int Delta,
    int BeforeOnHand, int AfterOnHand, int Reserved, int BeforeVersion, int AfterVersion);
public sealed record InventoryAdjustmentResponse(Guid OperationId, Guid ActorId, DateTimeOffset CreatedAt,
    string Reason, IReadOnlyList<InventoryAdjustmentLineResponse> Lines)
{
    public static InventoryAdjustmentResponse From(InventoryAdjustmentBatch batch) => new(batch.Id, batch.ActorId,
        batch.CreatedAt, batch.Reason, batch.Lines.OrderBy(l => l.VariantId.ToString("D"), StringComparer.Ordinal)
            .Select(l => new InventoryAdjustmentLineResponse(l.VariantId, l.Sku, l.Delta, l.BeforeOnHand,
                l.AfterOnHand, l.Reserved, l.BeforeVersion, l.AfterVersion)).ToArray());
}
