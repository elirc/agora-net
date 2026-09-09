using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public class InventoryReorderPolicy
{
    public const int DefaultThreshold = 5;
    public const int DefaultTargetLevel = 5;
    public Guid ProductVariantId { get; private set; }
    public int Threshold { get; private set; }
    public int TargetLevel { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private InventoryReorderPolicy() { }
    public InventoryReorderPolicy(Guid variantId, int threshold, int targetLevel, DateTimeOffset now)
    {
        Validate(threshold, targetLevel);
        ProductVariantId = variantId;
        Threshold = threshold;
        TargetLevel = targetLevel;
        UpdatedAt = now;
    }

    public void Replace(int threshold, int targetLevel, DateTimeOffset now)
    {
        Validate(threshold, targetLevel);
        Threshold = threshold;
        TargetLevel = targetLevel;
        UpdatedAt = now;
        Version = checked(Version + 1);
    }

    private static void Validate(int threshold, int targetLevel)
    {
        if (threshold < 0 || threshold > targetLevel || targetLevel > 1_000_000)
            throw new DomainException("Reorder policy requires 0 <= threshold <= targetLevel <= 1,000,000.");
    }
}
