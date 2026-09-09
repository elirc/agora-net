using Agora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Agora.Tests.Integration;

/// <summary>Shared in-memory database with a keepalive, but a distinct SQLite connection per request/worker scope.</summary>
internal sealed class WebhookApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=webhook-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection? _keepalive;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing"); builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AgoraDbContext>>(); services.RemoveAll<DbContextOptions>();
            _keepalive = new SqliteConnection(_connectionString); _keepalive.Open();
            services.AddDbContext<AgoraDbContext>(options => options.UseSqlite(_connectionString));
            using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>(); db.Database.EnsureCreated(); AgoraDbSeeder.SeedAsync(db).GetAwaiter().GetResult();
        });
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); _keepalive?.Dispose(); }
}
