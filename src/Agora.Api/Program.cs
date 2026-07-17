using System.Text;
using System.Threading.RateLimiting;
using Agora.Api;
using Agora.Api.Auth;
using Agora.Api.Health;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Request logging: one structured line per request with timing.
builder.Services.AddHttpLogging(logging =>
    logging.LoggingFields = HttpLoggingFields.RequestMethod
                            | HttpLoggingFields.RequestPath
                            | HttpLoggingFields.ResponseStatusCode
                            | HttpLoggingFields.Duration);

builder.Services.AddControllers(options =>
    options.Filters.Add<Agora.Api.Filters.DomainExceptionFilter>());

// RFC 7807 responses for unhandled exceptions and bare status codes.
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<AgoraDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("Default") ?? "Data Source=agora.db"));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
builder.Services.AddSingleton<IWebhookSender, FakeWebhookSender>();
builder.Services.AddScoped<WebhookService>();
builder.Services.AddScoped<TaxService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ReturnService>();
builder.Services.AddScoped<FulfillmentService>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// Checkout is rate limited per client (fixed window, configurable).
var checkoutRateLimit = builder.Configuration
    .GetSection(CheckoutRateLimitOptions.SectionName)
    .Get<CheckoutRateLimitOptions>() ?? new CheckoutRateLimitOptions();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("checkout", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = checkoutRateLimit.PermitLimit,
                Window = TimeSpan.FromSeconds(checkoutRateLimit.WindowSeconds),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.UseHttpLogging();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

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

// Readiness (with DB probe); the /health controller stays as the liveness probe.
app.MapHealthChecks("/health/ready");

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
