using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
    options.Filters.Add<Agora.Api.Filters.DomainExceptionFilter>());

builder.Services.AddDbContext<AgoraDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("Default") ?? "Data Source=agora.db"));

var app = builder.Build();

// Integration tests provide their own (in-memory) database.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AgoraDbContext>();
    db.Database.Migrate();

    if (app.Environment.IsDevelopment())
    {
        await AgoraDbSeeder.SeedAsync(db);
    }
}

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
