using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Infrastructure.Services;

public sealed record InventoryAdjustmentResult(InventoryAdjustmentBatch Receipt, bool Replayed);

public class InventoryAdjustmentService(AgoraDbContext db, IServiceScopeFactory scopes, TimeProvider clock)
{
    public async Task<InventoryAdjustmentResult> ApplyAsync(Guid actorId, InventoryAdjustmentCommand command, CancellationToken ct = default)
    {
        if (actorId == Guid.Empty) throw new DomainException("An actor is required for a stock adjustment.");
        var existing = await Read(db, command.OperationId, ct);
        if (existing is not null) return Replay(existing, command);
        try
        {
            // SQLite's default transaction obtains a write reservation before the recheck.
            // Receipt uniqueness and inventory tokens remain the final persistence safeguards.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            existing = await Read(db, command.OperationId, ct);
            if (existing is not null)
            {
                var replay = Replay(existing, command);
                await transaction.CommitAsync(ct);
                return replay;
            }
            var ids = command.Lines.Select(l => l.VariantId).ToArray();
            var stocks = await db.InventoryItems.Include(i => i.ProductVariant).Where(i => ids.Contains(i.ProductVariantId))
                .ToDictionaryAsync(i => i.ProductVariantId, ct);
            if (stocks.Count != ids.Length) throw new NotFoundException("Every adjustment variant must have an inventory record.");
            var proposed = new Dictionary<Guid, int>();
            foreach (var line in command.Lines)
            {
                var stock = stocks[line.VariantId];
                if (stock.Version != line.ExpectedVersion)
                    throw new InventoryAdjustmentConflictException("A stock revision changed. Reload all lines before submitting a new operation.");
                var after = checked((long)stock.QuantityOnHand + line.Delta);
                if (after < stock.QuantityReserved || after > 1_000_000 || after < 0 || stock.Version == int.MaxValue)
                    throw new InvalidInventoryAdjustmentException("Every resulting on-hand balance must be between reserved stock and 1,000,000, with an available next revision.");
                proposed.Add(line.VariantId, checked((int)after));
            }
            var receiptLines = new List<InventoryAdjustmentLine>();
            foreach (var line in command.Lines)
            {
                var stock = stocks[line.VariantId];
                var before = stock.QuantityOnHand;
                var beforeVersion = stock.Version;
                stock.SetStock(proposed[line.VariantId]);
                receiptLines.Add(new InventoryAdjustmentLine(command.OperationId, line.VariantId, stock.ProductVariant!.Sku,
                    line.Delta, before, stock.QuantityOnHand, stock.QuantityReserved, beforeVersion, stock.Version));
            }
            var batch = new InventoryAdjustmentBatch(command, actorId, clock.GetUtcNow(), receiptLines);
            db.InventoryAdjustmentBatches.Add(batch);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new InventoryAdjustmentResult(batch, false);
        }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        {
            // The failed transaction is disposed before entering this handler. Never reuse its tracked graph.
            return await ReadCommittedWinner(command, ct);
        }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteErrorCode: 5 or 6 })
        { throw new InventoryAdjustmentConflictException("Inventory is busy. Retry this same operation ID to recover its receipt safely."); }
        catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6)
        { throw new InventoryAdjustmentConflictException("Inventory is busy. Retry this same operation ID to recover its receipt safely."); }
    }

    private async Task<InventoryAdjustmentResult> ReadCommittedWinner(InventoryAdjustmentCommand command, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var fresh = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();
        var winner = await Read(fresh, command.OperationId, ct);
        if (winner is null) throw new InventoryAdjustmentConflictException("A competing write conflicted with this stock operation.");
        return Replay(winner, command);
    }

    private static InventoryAdjustmentResult Replay(InventoryAdjustmentBatch receipt, InventoryAdjustmentCommand command)
    {
        if (receipt.Fingerprint != command.Fingerprint)
            throw new InventoryAdjustmentConflictException("This operation ID was already used with different normalized content.");
        return new InventoryAdjustmentResult(receipt, true);
    }

    private static Task<InventoryAdjustmentBatch?> Read(AgoraDbContext context, Guid operationId, CancellationToken ct) =>
        context.InventoryAdjustmentBatches.AsNoTracking().Include(b => b.Lines).SingleOrDefaultAsync(b => b.Id == operationId, ct);
}
