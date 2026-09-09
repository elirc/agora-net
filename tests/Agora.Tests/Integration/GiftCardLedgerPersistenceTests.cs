using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class GiftCardLedgerPersistenceTests
{
    [Fact]
    public async Task Upgrade_records_current_balance_and_current_version_without_reconstructing_past_transactions()
    {
        await using var store = new Store(); Guid oldId; Guid spentId;
        await using (var db = store.Context())
        {
            await db.Database.MigrateAsync();
            var old = new GiftCard(100); old.Redeem(70); old.Credit(5); oldId = old.Id;
            var spent = new GiftCard(25); spent.Redeem(25); spentId = spent.Id;
            db.GiftCards.AddRange(old, spent); await db.SaveChangesAsync();
            await db.GetService<IMigrator>().MigrateAsync("20260908214457_OperationalHistory");
        }
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await using (var upgraded = store.Context())
        {
            await upgraded.Database.MigrateAsync(); Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
            var opening = await upgraded.GiftCardEntries.SingleAsync(e => e.GiftCardId == oldId);
            Assert.Equal((GiftCardEntryKind.OpeningBalance, 35m, 35m, 2), (opening.Kind, opening.Amount, opening.BalanceAfter, opening.RecordedVersion));
            Assert.InRange(opening.RecordedAt, before, DateTimeOffset.UtcNow); Assert.Null(opening.SourceOrderId); Assert.Null(opening.SourceReturnId);
            var card = await upgraded.GiftCards.SingleAsync(g => g.Id == oldId);
            Assert.Equal((100m, 35m, 2), (card.InitialBalance, card.Balance, card.Version));
            var zero = await upgraded.GiftCardEntries.SingleAsync(e => e.GiftCardId == spentId);
            Assert.Equal((0m, 0m, 1), (zero.Amount, zero.BalanceAfter, zero.RecordedVersion));
            GiftCardAccounting.Redeem(upgraded, card, 10, Guid.NewGuid(), DateTimeOffset.UtcNow); await upgraded.SaveChangesAsync();
        }
        await using (var fresh = store.Context())
        {
            var entries = await fresh.GiftCardEntries.AsNoTracking().Where(e => e.GiftCardId == oldId).OrderBy(e => e.RecordedVersion).ToListAsync();
            Assert.Equal(new[] { 2, 3 }, entries.Select(e => e.RecordedVersion)); Assert.Equal(new[] { 35m, -10m }, entries.Select(e => e.Amount));
            Assert.Equal(25m, (await fresh.GiftCards.SingleAsync(g => g.Id == oldId)).Balance);
            fresh.GiftCards.Remove(await fresh.GiftCards.SingleAsync(g => g.Id == oldId));
            await Assert.ThrowsAsync<DbUpdateException>(() => fresh.SaveChangesAsync());
        }
        await using var retained = store.Context(); Assert.Equal(2, await retained.GiftCards.CountAsync()); Assert.Equal(3, await retained.GiftCardEntries.CountAsync());
    }

    [Fact]
    public async Task Forced_entry_save_failure_rolls_back_balance_and_issuance_without_retrying_an_external_action()
    {
        await using var store = new Store(); var id = await store.Issue(50);
        await using (var setup = store.Context())
            await setup.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailGiftEntry AFTER INSERT ON GiftCardEntries BEGIN SELECT RAISE(ABORT, 'forced ledger failure'); END;");
        await using (var failed = store.Context())
        {
            var card = await failed.GiftCards.SingleAsync(); GiftCardAccounting.Redeem(failed, card, 20, Guid.NewGuid(), DateTimeOffset.UtcNow);
            await Assert.ThrowsAsync<DbUpdateException>(() => failed.SaveChangesAsync());
        }
        await using (var failedIssue = store.Context())
        {
            GiftCardAccounting.Issue(failedIssue, new GiftCard(25), DateTimeOffset.UtcNow);
            await Assert.ThrowsAsync<DbUpdateException>(() => failedIssue.SaveChangesAsync());
        }
        await using (var verify = store.Context())
        {
            var card = await verify.GiftCards.SingleAsync(); Assert.Equal((id, 50m, 0), (card.Id, card.Balance, card.Version));
            Assert.Single(await verify.GiftCardEntries.ToListAsync());
            await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER FailGiftEntry;");
        }
        await using (var newLocalOperation = store.Context())
        {
            var card = await newLocalOperation.GiftCards.SingleAsync(); var orderId = Guid.NewGuid();
            GiftCardAccounting.Redeem(newLocalOperation, card, 20, orderId, DateTimeOffset.UtcNow);
            GiftCardAccounting.Credit(newLocalOperation, card, 5, orderId, Guid.NewGuid(), DateTimeOffset.UtcNow);
            await newLocalOperation.SaveChangesAsync();
        }
        await using var fresh = store.Context(); var entries = await fresh.GiftCardEntries.OrderBy(e => e.RecordedVersion).ToListAsync();
        Assert.Equal(new[] { 50m, -20m, 5m }, entries.Select(e => e.Amount)); Assert.Equal(new[] { 50m, 30m, 35m }, entries.Select(e => e.BalanceAfter));
        Assert.Equal(35m, (await fresh.GiftCards.SingleAsync()).Balance);
    }

    [Fact]
    public async Task Competing_redemptions_from_one_observed_balance_save_one_change_and_one_entry()
    {
        await using var store = new Store(); await store.Issue(50);
        var both = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var arrivals = 0;
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var db = store.Context(); var card = await db.GiftCards.SingleAsync();
            if (Interlocked.Increment(ref arrivals) == 2) both.TrySetResult(true);
            await both.Task.WaitAsync(TimeSpan.FromSeconds(30));
            GiftCardAccounting.Redeem(db, card, 40, Guid.NewGuid(), DateTimeOffset.UtcNow);
            try { await db.SaveChangesAsync(); return "saved"; }
            catch (DbUpdateException) { return "conflict"; }
        })));
        Assert.Single(results, r => r == "saved"); Assert.Single(results, r => r == "conflict");
        await using var fresh = store.Context(); var current = await fresh.GiftCards.SingleAsync();
        Assert.Equal((10m, 1), (current.Balance, current.Version)); Assert.Equal(2, await fresh.GiftCardEntries.CountAsync());
        Assert.Equal(-40m, (await fresh.GiftCardEntries.SingleAsync(e => e.Kind == GiftCardEntryKind.Redeemed)).Amount);
        Assert.Throws<InvalidGiftCardException>(() => GiftCardAccounting.Redeem(fresh, current, 11, Guid.NewGuid(), DateTimeOffset.UtcNow));
        await fresh.SaveChangesAsync(); Assert.Equal(2, await fresh.GiftCardEntries.CountAsync());
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-gift-ledger-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=30").Options);
        public async Task<Guid> Issue(decimal amount)
        {
            await using var db = Context(); await db.Database.EnsureCreatedAsync(); var card = new GiftCard(amount);
            GiftCardAccounting.Issue(db, card, DateTimeOffset.UnixEpoch); await db.SaveChangesAsync(); return card.Id;
        }
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
}
