using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class IntegrationKeysAndHistoryMigrationTests
{
    [Fact]
    public async Task Upgrade_preserves_customer_history_adds_seek_index_and_invents_no_machine_credentials()
    {
        var path = Path.Combine(Path.GetTempPath(), "agora-access-upgrade-" + Guid.NewGuid().ToString("N") + ".db");
        AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options);
        var owner = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        try
        {
            await using (var before = Context())
            {
                await before.GetService<IMigrator>().MigrateAsync("20260908224638_SellingWarehouseAndAccessPolicies");
                before.Customers.Add(new Customer { Id = owner, Email = "legacy-history@example.test", FullName = "Legacy owner" });
                before.Orders.Add(new Order { Id = orderId, CustomerId = owner, Number = "LEGACY-HISTORY-1", Email = "legacy-history@example.test" });
                await before.SaveChangesAsync();
            }

            await using var after = Context();
            await after.Database.MigrateAsync();

            var order = await after.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal(owner, order.CustomerId);
            Assert.Equal("LEGACY-HISTORY-1", order.Number);
            Assert.Empty(await after.Set<IntegrationApiKey>().ToArrayAsync());
            await after.Database.OpenConnectionAsync();
            using var command = after.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'IX_Orders_CustomerId_CreatedAt_Number'";
            var sql = Assert.IsType<string>(await command.ExecuteScalarAsync());
            Assert.Contains("CustomerId", sql);
            Assert.Contains("CreatedAt\" DESC", sql);
            Assert.Contains("Number\" DESC", sql);
        }
        finally { File.Delete(path); }
    }
}
