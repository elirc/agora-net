using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Tests.Integration;

public sealed class ReportExportPersistenceTests
{
    [Fact]
    public async Task Upgrade_preserves_existing_customer_and_does_not_invent_export_jobs()
    {
        await using var store = new Store();
        Guid customerId;
        await using (var latest = store.Context())
        {
            await latest.Database.MigrateAsync();
            var customer = new Customer { Email = "report-upgrade@example.test", PasswordHash = "h" };
            latest.Add(customer);
            await latest.SaveChangesAsync();
            customerId = customer.Id;
        }
        await using (var old = store.Context())
            await old.GetService<IMigrator>().MigrateAsync("20260908224638_SellingWarehouseAndAccessPolicies");
        await using var upgraded = store.Context();
        await upgraded.Database.MigrateAsync();
        Assert.True(await upgraded.Customers.AnyAsync(x => x.Id == customerId));
        Assert.Empty(await upgraded.Set<ReportExportJob>().ToArrayAsync());
        Assert.Empty(await upgraded.Set<ReportExportArtifact>().ToArrayAsync());
    }

    [Fact]
    public async Task Independent_claims_allow_one_generation_writer()
    {
        await using var store = new Store(); var id = await store.Seed();
        await using var first = store.Context(); await using var second = store.Context();
        var a = await first.Set<ReportExportJob>().SingleAsync(x => x.Id == id);
        var b = await second.Set<ReportExportJob>().SingleAsync(x => x.Id == id);
        a.Claim(DateTimeOffset.UnixEpoch); b.Claim(DateTimeOffset.UnixEpoch);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var fresh = store.Context(); var saved = await fresh.Set<ReportExportJob>().SingleAsync();
        Assert.Equal(1, saved.ClaimCount); Assert.Equal(1, saved.LeaseGeneration);
    }

    [Fact]
    public async Task Cancellation_wins_against_stale_publication_and_no_artifact_is_committed()
    {
        await using var store = new Store(); var id = await store.Seed();
        long generation; await using (var claim = store.Context())
        { var job = await claim.Set<ReportExportJob>().SingleAsync(); generation = job.Claim(DateTimeOffset.UnixEpoch); await claim.SaveChangesAsync(); }
        await using var worker = store.Context(); await using var canceller = store.Context();
        var stale = await worker.Set<ReportExportJob>().SingleAsync(); var current = await canceller.Set<ReportExportJob>().SingleAsync();
        current.Cancel(DateTimeOffset.UnixEpoch.AddSeconds(1)); await canceller.SaveChangesAsync();
        Assert.True(stale.Publish(generation, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(2)));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => worker.SaveChangesAsync());
        await using var fresh = store.Context(); Assert.True((await fresh.Set<ReportExportJob>().SingleAsync()).CancellationRequested);
        Assert.Empty(await fresh.Set<ReportExportArtifact>().ToArrayAsync());
    }

    [Fact]
    public async Task Expired_lease_is_recoverable_after_process_restart()
    {
        await using var store = new Store(); await store.Seed();
        await using (var crashed = store.Context()) { var j = await crashed.Set<ReportExportJob>().SingleAsync(); j.Claim(DateTimeOffset.UnixEpoch); await crashed.SaveChangesAsync(); }
        await using var restarted = store.Context(); var recovered = await restarted.Set<ReportExportJob>().SingleAsync();
        var generation = recovered.Claim(DateTimeOffset.UnixEpoch.AddMinutes(2)); await restarted.SaveChangesAsync();
        Assert.Equal(2, generation); Assert.Equal(2, recovered.ClaimCount);
    }

    [Fact]
    public async Task Cleanup_removes_only_twenty_five_then_progresses_past_retained_job_metadata()
    {
        await using var store = new Store();
        await using (var db = store.Context())
        {
            await db.Database.EnsureCreatedAsync();
            var admin = new Customer { Email = "cleanup@example.test", PasswordHash = "h", Role = CustomerRole.Admin };
            db.Add(admin);
            for (var i = 0; i < 26; i++)
            {
                var job = new ReportExportJob(admin.Id, DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch.AddDays(1), DateTimeOffset.UnixEpoch);
                var generation = job.Claim(DateTimeOffset.UnixEpoch);
                Assert.True(job.Publish(generation, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
                db.AddRange(job, new ReportExportArtifact(job.Id, [1], new string('A', 64)));
            }
            await db.SaveChangesAsync();
        }
        var services = new ServiceCollection()
            .AddDbContext<AgoraDbContext>(o => o.UseSqlite(store.ConnectionString))
            .BuildServiceProvider();
        var runner = new Agora.Infrastructure.Services.ReportExportRunner(
            services.GetRequiredService<IServiceScopeFactory>(), new FixedClock(DateTimeOffset.UnixEpoch.AddHours(25)));
        Assert.Equal(25, await runner.CleanupAsync());
        Assert.Equal(1, await runner.CleanupAsync());
        Assert.Equal(0, await runner.CleanupAsync());
        await using var fresh = store.Context();
        Assert.Equal(26, await fresh.Set<ReportExportJob>().CountAsync());
        Assert.Empty(await fresh.Set<ReportExportArtifact>().ToArrayAsync());
    }

    [Fact]
    public async Task Runner_accepts_exactly_ten_thousand_orders_and_fails_ten_thousand_one()
    {
        await using var store = new Store(); Guid owner;
        await using (var db = store.Context())
        {
            await db.Database.EnsureCreatedAsync();
            var admin = new Customer { Email = "limits@example.test", PasswordHash = "h", Role = CustomerRole.Admin };
            owner = admin.Id; db.Add(admin);
            var orders = Enumerable.Range(0, 10001).Select(i =>
            {
                var paid = i == 10000 ? DateTimeOffset.UnixEpoch.AddSeconds(2) : DateTimeOffset.UnixEpoch.AddSeconds(1);
                var order = new Order { Number = $"LIMIT-{i:D5}", Email = "guest@example.test", CreatedAt = paid };
                order.MarkPaid("txn", paid); return order;
            });
            db.Orders.AddRange(orders);
            db.Add(new ReportExportJob(owner, DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddSeconds(2), DateTimeOffset.UnixEpoch));
            await db.SaveChangesAsync();
        }
        using var services = new ServiceCollection()
            .AddDbContext<AgoraDbContext>(o => o.UseSqlite(store.ConnectionString)).BuildServiceProvider();
        var runner = new Agora.Infrastructure.Services.ReportExportRunner(
            services.GetRequiredService<IServiceScopeFactory>(), new FixedClock(DateTimeOffset.UnixEpoch.AddSeconds(3)));
        Assert.Equal(1, await runner.RunOnceAsync());
        await using (var inspect = store.Context())
        {
            Assert.Equal(ReportExportStatus.Succeeded, (await inspect.Set<ReportExportJob>().SingleAsync()).Status);
            inspect.Add(new ReportExportJob(owner, DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddSeconds(3), DateTimeOffset.UnixEpoch));
            await inspect.SaveChangesAsync();
        }
        Assert.Equal(1, await runner.RunOnceAsync());
        await using var final = store.Context();
        Assert.Contains(await final.Set<ReportExportJob>().ToArrayAsync(),
            j => j.Status == ReportExportStatus.Failed && j.FailureCode == "OrderLimitExceeded");
    }

    [Fact]
    public async Task Csv_quotes_untrusted_cells_keeps_currencies_separate_and_uses_historical_refunded_totals()
    {
        await using var store = new Store();
        await using (var db = store.Context())
        {
            await db.Database.EnsureCreatedAsync();
            var admin = new Customer { Email = "csv@example.test", PasswordHash = "h", Role = CustomerRole.Admin };
            var formula = PaidOrder("=2+2,\"quoted\"", "USD", 12.34m, 2);
            formula.Refund(DateTimeOffset.UnixEpoch.AddSeconds(2));
            var euros = PaidOrder("EURO", "EUR", 9.87m, 1);
            db.AddRange(admin, formula, euros, new ReportExportJob(admin.Id, DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddMinutes(1), DateTimeOffset.UnixEpoch));
            await db.SaveChangesAsync();
        }
        using var services = new ServiceCollection().AddDbContext<AgoraDbContext>(o =>
            o.UseSqlite(store.ConnectionString)).BuildServiceProvider();
        var runner = new Agora.Infrastructure.Services.ReportExportRunner(
            services.GetRequiredService<IServiceScopeFactory>(), new FixedClock(DateTimeOffset.UnixEpoch.AddSeconds(3)));
        Assert.Equal(1, await runner.RunOnceAsync());
        await using var check = store.Context();
        var csv = System.Text.Encoding.UTF8.GetString((await check.Set<ReportExportArtifact>().SingleAsync()).Content);
        Assert.Contains("\"'=2+2,\"\"quoted\"\"\"", csv);
        Assert.Contains(",\"Refunded\",\"USD\",2,12.34", csv);
        Assert.Contains(",\"Paid\",\"EUR\",1,9.87", csv);
    }

    [Fact]
    public async Task Csv_first_byte_past_ten_mib_fails_without_publishing_an_artifact()
    {
        await using var store = new Store();
        await using (var db = store.Context())
        {
            await db.Database.EnsureCreatedAsync();
            var admin = new Customer { Email = "bytes@example.test", PasswordHash = "h", Role = CustomerRole.Admin };
            db.Add(admin);
            db.Orders.AddRange(Enumerable.Range(0, 10_000).Select(i =>
                PaidOrder("=" + new string('x', 1_050) + i.ToString("D5"), "USD", 1m, 1)));
            db.Add(new ReportExportJob(admin.Id, DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddMinutes(1), DateTimeOffset.UnixEpoch));
            await db.SaveChangesAsync();
        }
        using var services = new ServiceCollection().AddDbContext<AgoraDbContext>(o =>
            o.UseSqlite(store.ConnectionString)).BuildServiceProvider();
        var runner = new Agora.Infrastructure.Services.ReportExportRunner(
            services.GetRequiredService<IServiceScopeFactory>(), new FixedClock(DateTimeOffset.UnixEpoch.AddSeconds(3)));
        Assert.Equal(1, await runner.RunOnceAsync());
        await using var check = store.Context();
        Assert.Equal("ByteLimitExceeded", (await check.Set<ReportExportJob>().SingleAsync()).FailureCode);
        Assert.Empty(await check.Set<ReportExportArtifact>().ToArrayAsync());
    }

    private static Order PaidOrder(string number, string currency, decimal total, int quantity)
    {
        var order = new Order
        {
            Number = number, Email = "guest@example.test", Currency = currency,
            Subtotal = total, Total = total, CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
            Items = [new OrderItem { ProductVariantId = Guid.NewGuid(), Sku = "SKU", ProductName = "Product",
                VariantName = "Default", UnitPrice = total / quantity, Quantity = quantity, LineTotal = total }]
        };
        order.MarkPaid("txn", DateTimeOffset.UnixEpoch.AddSeconds(1));
        return order;
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "agora-report-export-" + Guid.NewGuid().ToString("N") + ".db");
        public string ConnectionString => $"Data Source={path};Pooling=False;Default Timeout=30";
        public AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>()
            .UseSqlite(ConnectionString).Options);
        public async Task<Guid> Seed()
        {
            await using var db = Context(); await db.Database.EnsureCreatedAsync();
            var admin = new Customer { Email = Guid.NewGuid().ToString("N") + "@example.test", PasswordHash = "h", Role = CustomerRole.Admin };
            var job = new ReportExportJob(admin.Id, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), DateTimeOffset.UnixEpoch);
            db.AddRange(admin, job); await db.SaveChangesAsync(); return job.Id;
        }
        public ValueTask DisposeAsync() { if (File.Exists(path)) File.Delete(path); return ValueTask.CompletedTask; }
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
