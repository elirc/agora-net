using System.Text;
using Agora.Api.Auth;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.Configure<CheckoutOptions>(
    builder.Configuration.GetSection(CheckoutOptions.SectionName));
builder.Services.AddSingleton<ITaxCalculator, FlatRateTaxCalculator>();
builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<OrderService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

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
