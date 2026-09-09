using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class ShippingFeaturesPersistenceTests
{
    [Fact]
    public async Task Upgrade_preserves_existing_methods_and_seeds_disabled_calendar_without_inventing_policies()
    {
        await using var store = new Store(); Guid methodId;
        await using (var old = store.Context())
        {
            await old.Database.MigrateAsync();
            await old.GetService<IMigrator>().MigrateAsync("20260908223533_CatalogImportStaging");
            var method = new ShippingMethod { Code = "legacy-ship", Name = "Legacy", IsActive = true, MinDays = 2, MaxDays = 5 };
            old.ShippingMethods.Add(method); await old.SaveChangesAsync(); methodId = method.Id;
        }
        await using var upgraded = store.Context(); await upgraded.Database.MigrateAsync();
        Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
        var methodAfter = await upgraded.ShippingMethods.SingleAsync(m => m.Id == methodId);
        Assert.Equal((2, 5), (methodAfter.MinDays, methodAfter.MaxDays));
        Assert.False(await upgraded.Set<ShippingEligibilityPolicy>().AnyAsync(p => p.ShippingMethodId == methodId));
        var calendar = await upgraded.Set<DeliveryCalendar>().Include(c => c.Closures).SingleAsync(c => c.Id == 1);
        Assert.False(calendar.Enabled); Assert.Equal(0, calendar.Revision); Assert.Empty(calendar.Closures);
    }

    [Fact]
    public async Task Competing_policy_creates_and_calendar_replacements_cannot_silently_overwrite_each_other()
    {
        await using var store = new Store(); Guid methodId;
        await using (var setup = store.Context())
        {
            await setup.Database.EnsureCreatedAsync();
            var method = new ShippingMethod { Code = "race-ship", Name = "Race", IsActive = true };
            setup.ShippingMethods.Add(method); await setup.SaveChangesAsync(); methodId = method.Id;
        }
        await using (var first = store.Context()) await using (var second = store.Context())
        {
            first.Add(new ShippingEligibilityPolicy(methodId, ["US"], 100));
            second.Add(new ShippingEligibilityPolicy(methodId, ["CA"], 200));
            await first.SaveChangesAsync(); await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        }
        await using (var first = store.Context()) await using (var second = store.Context())
        {
            var a = await first.Set<DeliveryCalendar>().Include(c => c.Closures).SingleAsync();
            var b = await second.Set<DeliveryCalendar>().Include(c => c.Closures).SingleAsync();
            a.Replace(true, 600, [new DateOnly(2026, 12, 25)]);
            b.Replace(true, 900, [new DateOnly(2026, 12, 26)]);
            await first.SaveChangesAsync(); await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }
        await using var check = store.Context();
        Assert.Equal(["US"], (await check.Set<ShippingEligibilityPolicy>().SingleAsync()).Countries());
        var calendar = await check.Set<DeliveryCalendar>().Include(c => c.Closures).SingleAsync();
        Assert.Equal(600, calendar.CutoffUtcMinute); Assert.Equal([new DateOnly(2026, 12, 25)], calendar.Closures.Select(c => c.Date));
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-shipping-features-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>()
            .UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=30").Options);
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
}
