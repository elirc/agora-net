using System.Data.Common;
using System.Security.Claims;
using Agora.Api.Contracts;
using Agora.Api.Controllers;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class CheckoutPreferencePersistenceTests
{
    [Fact]
    public async Task Create_only_race_and_stale_update_cannot_overwrite_another_preference_write()
    {
        await using var store = new Store(); var owner = await store.Seed(); var barrier = new StartTogether();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var db = store.Context(barrier);
            var controller = new CheckoutPreferencesController(db) { ControllerContext = new ControllerContext
            { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", owner.ToString())], "Test")) } } };
            return (await controller.Put(new PutCheckoutPreferenceRequest(null, null, null), default)).Result;
        })));
        Assert.Single(results, r => r is OkObjectResult); Assert.Single(results, r => r is ConflictObjectResult);
        await using var winner = store.Context(); await using var loser = store.Context();
        var first = await winner.CheckoutPreferences.SingleAsync(); var stale = await loser.CheckoutPreferences.SingleAsync();
        first.Replace(null, "winner"); await winner.SaveChangesAsync();
        stale.Replace(null, "loser"); await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => loser.SaveChangesAsync());
        await using var fresh = store.Context(); var actual = await fresh.CheckoutPreferences.SingleAsync();
        Assert.Equal(("winner", 1L), (actual.ShippingMethodCode, actual.Version));
    }

    [Fact]
    public async Task Upgrade_keeps_old_discount_redeemable_and_template_intent_and_sets_deleted_address_to_null()
    {
        await using var store = new Store(); var owner = await store.Seed(migrations: true); Guid addressId;
        await using (var old = store.Context())
        {
            var address = new CustomerAddress { CustomerId = owner, Label = "Existing", Address = CheckoutQuoteApiTests.Address.ToAddress() }; addressId = address.Id;
            old.AddRange(address, new DiscountCode { Code = "EXISTING", Type = DiscountType.FixedAmount, Value = 1, IsActive = true },
                new CartTemplate(owner, "Existing template", [new(Guid.NewGuid(), 2, "HISTORICAL", "Product", "Variant")], DateTimeOffset.UnixEpoch));
            await old.SaveChangesAsync(); await old.GetService<IMigrator>().MigrateAsync("20260908212656_CartTemplates");
        }
        await using (var upgraded = store.Context())
        {
            await upgraded.Database.MigrateAsync(); Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
            Assert.Empty(await upgraded.CheckoutPreferences.ToListAsync());
            var discount = await upgraded.DiscountCodes.SingleAsync(); Assert.Null(discount.StartsAt); Assert.True(discount.IsRedeemable(DateTimeOffset.UtcNow));
            Assert.Equal("HISTORICAL", (await upgraded.CartTemplateLines.SingleAsync()).Sku);
            upgraded.CheckoutPreferences.Add(new CheckoutPreference(owner, addressId, null)); await upgraded.SaveChangesAsync();
        }
        await using (var removed = store.Context())
        {
            removed.CustomerAddresses.Remove(await removed.CustomerAddresses.SingleAsync()); await removed.SaveChangesAsync();
            Assert.Null((await removed.CheckoutPreferences.SingleAsync()).ShippingAddressId);
            removed.Customers.Remove(await removed.Customers.SingleAsync()); await removed.SaveChangesAsync();
            Assert.Empty(await removed.CheckoutPreferences.ToListAsync()); Assert.Empty(await removed.CartTemplates.ToListAsync());
            Assert.Equal(1, await removed.DiscountCodes.CountAsync());
        }
    }

    private sealed class StartTogether : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource<bool> _both = new(TaskCreationOptions.RunContinuationsAsynchronously); private int _arrivals;
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            var arrival = Interlocked.Increment(ref _arrivals);
            if (arrival <= 2) { if (arrival == 2) _both.TrySetResult(true); await _both.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
            return result;
        }
    }
    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-preferences-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={_path};Pooling=False;Default Timeout=30");
            if (interceptor is not null) options.AddInterceptors(interceptor); return new AgoraDbContext(options.Options);
        }
        public async Task<Guid> Seed(bool migrations = false)
        {
            await using var db = Context(); if (migrations) await db.Database.MigrateAsync(); else await db.Database.EnsureCreatedAsync();
            var owner = new Customer { Email = "preferences@example.test", FullName = "Existing owner", PasswordHash = "unused-test-hash" };
            db.Customers.Add(owner); await db.SaveChangesAsync(); return owner.Id;
        }
        public ValueTask DisposeAsync() { File.Delete(_path); return ValueTask.CompletedTask; }
    }
}
