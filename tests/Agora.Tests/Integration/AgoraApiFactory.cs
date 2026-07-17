using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Agora.Tests.Integration;

/// <summary>
/// Boots the API against a private in-memory SQLite database (one per factory),
/// schema-created and seeded with the dev catalog.
/// </summary>
public sealed class AgoraApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AgoraDbContext>>();
            services.RemoveAll<DbContextOptions>();

            _connection.Open();
            services.AddDbContext<AgoraDbContext>(options => options.UseSqlite(_connection));

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();
            db.Database.EnsureCreated();
            AgoraDbSeeder.SeedAsync(db).GetAwaiter().GetResult();
        });
    }

    /// <summary>Runs an action against a fresh DbContext scope (arrange/assert helpers).</summary>
    public async Task WithDbAsync(Func<AgoraDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();
        await action(db);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }
}
