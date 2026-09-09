using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public sealed class LoginSessionPersistenceTests
{
    [Fact]
    public async Task Upgrade_preserves_existing_customer_invents_no_sessions_and_allows_next_login_session()
    {
        var path = Path.Combine(Path.GetTempPath(), "agora-login-upgrade-" + Guid.NewGuid().ToString("N") + ".db");
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options;
        var customer = new Customer
        {
            Email = "pre-session@example.test",
            FullName = "Existing Customer",
            PasswordHash = "historical-hash",
        };

        try
        {
            // Build the current schema once, then exercise the real down/up route used
            // by the repository's migration verification convention.
            await using (var setup = new AgoraDbContext(options))
            {
                await setup.Database.MigrateAsync();
                await setup.GetService<IMigrator>().MigrateAsync("20260908222855_CategoryOptionSchemas");
                setup.Customers.Add(customer);
                await setup.SaveChangesAsync();
            }

            await using (var upgraded = new AgoraDbContext(options))
            {
                await upgraded.Database.MigrateAsync();
                Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
                var preserved = await upgraded.Customers.SingleAsync(c => c.Id == customer.Id);
                Assert.Equal("pre-session@example.test", preserved.Email);
                Assert.Empty(await upgraded.Set<LoginSession>().ToArrayAsync());

                var clock = new FixedAuthenticationClock(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
                var service = new AuthenticationSessionService(upgraded, clock);
                var session = service.Start(preserved, "First login after upgrade", clock.GetUtcNow(), clock.GetUtcNow().AddHours(1));
                await upgraded.SaveChangesAsync();

                Assert.Equal(session.Id, (await upgraded.Set<LoginSession>().SingleAsync()).Id);
                Assert.Equal(customer.Id, session.CustomerId);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class FixedAuthenticationClock(DateTimeOffset instant) : AuthenticationTimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
