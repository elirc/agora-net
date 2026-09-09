using System.Security.Cryptography;
using System.Text.Json;
using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public sealed record InventoryAdjustmentChange(Guid VariantId, int Delta, int ExpectedVersion);

public sealed class InventoryAdjustmentCommand
{
    public Guid OperationId { get; }
    public string Reason { get; }
    public IReadOnlyList<InventoryAdjustmentChange> Lines { get; }
    public string Fingerprint { get; }
    private InventoryAdjustmentCommand(Guid operationId, string reason, IReadOnlyList<InventoryAdjustmentChange> lines, string fingerprint)
    { OperationId = operationId; Reason = reason; Lines = lines; Fingerprint = fingerprint; }

    public static InventoryAdjustmentCommand Create(Guid operationId, string reason, IReadOnlyList<InventoryAdjustmentChange> lines)
    {
        var normalizedReason = reason.Trim();
        if (operationId == Guid.Empty || normalizedReason.Length is < 1 or > 200 || lines.Count is < 1 or > 50 ||
            lines.Any(l => l.VariantId == Guid.Empty || l.Delta is 0 or < -1_000_000 or > 1_000_000 || l.ExpectedVersion < 0) ||
            lines.Select(l => l.VariantId).Distinct().Count() != lines.Count)
            throw new DomainException("Use a nonempty operation ID, a 1–200 character reason, and 1–50 distinct valid stock corrections.");
        var ordered = lines.OrderBy(l => l.VariantId.ToString("D"), StringComparer.Ordinal).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            reason = normalizedReason,
            lines = ordered.Select(l => new { variantId = l.VariantId.ToString("D"), delta = l.Delta, expectedVersion = l.ExpectedVersion }),
        });
        return new InventoryAdjustmentCommand(operationId, normalizedReason, Array.AsReadOnly(ordered),
            Convert.ToHexString(SHA256.HashData(bytes)));
    }
}

/// <summary>Local stock-change receipt. Actor and variant IDs are historical values, not cascading catalog relationships.</summary>
public class InventoryAdjustmentBatch
{
    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string Reason { get; private set; } = "";
    public string Fingerprint { get; private set; } = "";
    public List<InventoryAdjustmentLine> Lines { get; private set; } = [];
    private InventoryAdjustmentBatch() { }
    public InventoryAdjustmentBatch(InventoryAdjustmentCommand command, Guid actorId, DateTimeOffset now,
        IEnumerable<InventoryAdjustmentLine> lines)
    {
        Id = command.OperationId;
        ActorId = actorId;
        CreatedAt = now;
        Reason = command.Reason;
        Fingerprint = command.Fingerprint;
        Lines = lines.ToList();
    }
}

public class InventoryAdjustmentLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid BatchId { get; private set; }
    public Guid VariantId { get; private set; }
    public string Sku { get; private set; } = "";
    public int Delta { get; private set; }
    public int BeforeOnHand { get; private set; }
    public int AfterOnHand { get; private set; }
    public int Reserved { get; private set; }
    public int BeforeVersion { get; private set; }
    public int AfterVersion { get; private set; }
    private InventoryAdjustmentLine() { }
    public InventoryAdjustmentLine(Guid batchId, Guid variantId, string sku, int delta,
        int beforeOnHand, int afterOnHand, int reserved, int beforeVersion, int afterVersion)
    {
        BatchId = batchId; VariantId = variantId; Sku = sku; Delta = delta;
        BeforeOnHand = beforeOnHand; AfterOnHand = afterOnHand; Reserved = reserved;
        BeforeVersion = beforeVersion; AfterVersion = afterVersion;
    }
}

public sealed class InventoryAdjustmentConflictException(string message) : DomainException(message);
public sealed class InvalidInventoryAdjustmentException(string message) : DomainException(message);
