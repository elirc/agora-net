using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class InventoryAdjustmentCommandTests
{
    [Fact]
    public void Fingerprint_ignores_order_and_reason_edge_space_but_preserves_each_semantic_input()
    {
        var operation = Guid.NewGuid();
        InventoryAdjustmentChange[] lines = [new(Guid.NewGuid(), -3, 1), new(Guid.NewGuid(), 4, 0)];
        var first = InventoryAdjustmentCommand.Create(operation, "  Stock count  ", lines);
        var same = InventoryAdjustmentCommand.Create(operation, "Stock count", lines.Reverse().ToArray());
        Assert.Equal(first.Fingerprint, same.Fingerprint);
        Assert.NotEqual(first.Fingerprint, InventoryAdjustmentCommand.Create(operation, "Stock recount", lines).Fingerprint);
        Assert.NotEqual(first.Fingerprint, InventoryAdjustmentCommand.Create(operation, "Stock count", [lines[0] with { Delta = -2 }, lines[1]]).Fingerprint);
        Assert.NotEqual(first.Fingerprint, InventoryAdjustmentCommand.Create(operation, "Stock count", [lines[0] with { ExpectedVersion = 2 }, lines[1]]).Fingerprint);
        lines[0] = lines[0] with { Delta = -100 };
        Assert.DoesNotContain(first.Lines, l => l.Delta == -100);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Shape_limits_reject_empty_duplicate_and_oversized_commands()
    {
        var id = Guid.NewGuid();
        InventoryAdjustmentChange line = new(Guid.NewGuid(), 1, 0);
        Assert.Throws<DomainException>(() => InventoryAdjustmentCommand.Create(Guid.Empty, "count", [line]));
        Assert.Throws<DomainException>(() => InventoryAdjustmentCommand.Create(id, "count", []));
        Assert.Throws<DomainException>(() => InventoryAdjustmentCommand.Create(id, "count", [line, line]));
        Assert.Throws<DomainException>(() => InventoryAdjustmentCommand.Create(id, "count", Enumerable.Range(0, 51).Select(_ => line with { VariantId = Guid.NewGuid() }).ToArray()));
        Assert.Equal(50, InventoryAdjustmentCommand.Create(id, new string('x', 200), Enumerable.Range(0, 50).Select(_ => line with { VariantId = Guid.NewGuid() }).ToArray()).Lines.Count);
    }
}
