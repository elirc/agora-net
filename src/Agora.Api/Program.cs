using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
    options.Filters.Add<Agora.Api.Filters.DomainExceptionFilter>());

// RFC 7807 responses for unhandled exceptions and bare status codes.
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<AgoraDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("Default") ?? "Data Source=agora.db"));

builder.Services.Configure<CheckoutOptions>(
    builder.Configuration.GetSection(CheckoutOptions.SectionName));
builder.Services.AddSingleton<ITaxCalculator, FlatRateTaxCalculator>();
builder.Services.AddSingleton<IShippingCalculator, FlatRateShippingCalculator>();
builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<OrderService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

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
