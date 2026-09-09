using System.Text;
using System.Threading.RateLimiting;
using Agora.Api;
using Agora.Api.Auth;
using Agora.Api.Health;
using Agora.Domain.Services;
using Agora.Infrastructure.Persistence;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
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
builder.Services.AddSingleton<AuthenticationTimeProvider>();
if (builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
else
    builder.Services.AddDataProtection().SetApplicationName("Agora.OrderHistory")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(
            builder.Configuration["DataProtection:KeyDirectory"] ?? "data-protection-keys", builder.Environment.ContentRootPath)));
builder.Services.AddSingleton<Agora.Api.Queries.OrderHistoryCursorProtector>();
builder.Services.AddScoped<Agora.Api.Queries.OrderHistoryFeedQuery>();
builder.Services.AddScoped<AuthenticationSessionService>();
builder.Services.AddScoped<GuestOrderAccessService>();
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
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal!;
                if (!Guid.TryParse(principal.FindFirst("sid")?.Value, out var sessionId)
                    || !Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var customerId)
                    || string.IsNullOrWhiteSpace(principal.FindFirst("role")?.Value)
                    || !long.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Exp)?.Value, out var expiresSeconds))
                {
                    context.Fail("The login session claim is missing or invalid.");
                    return;
                }
                DateTimeOffset expiry;
                try { expiry = DateTimeOffset.FromUnixTimeSeconds(expiresSeconds); }
                catch (ArgumentOutOfRangeException)
                {
                    context.Fail("The login session expiry is invalid.");
                    return;
                }
                var sessions = context.HttpContext.RequestServices.GetRequiredService<AuthenticationSessionService>();
                if (!await sessions.IsAuthorizedAsync(sessionId, customerId, principal.FindFirst("role")!.Value,
                        expiry, context.HttpContext.RequestAborted))
                    context.Fail("The login session is no longer authorized.");
            },
        };
    });
builder.Services.AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, IntegrationKeyAuthenticationHandler>(
        IntegrationKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(IntegrationKeyAuthenticationHandler.CatalogPolicy, policy => policy
        .AddAuthenticationSchemes(IntegrationKeyAuthenticationHandler.SchemeName).RequireAuthenticatedUser().RequireClaim("scope", "CatalogRead"));
    options.AddPolicy(IntegrationKeyAuthenticationHandler.InventoryPolicy, policy => policy
        .AddAuthenticationSchemes(IntegrationKeyAuthenticationHandler.SchemeName).RequireAuthenticatedUser().RequireClaim("scope", "InventoryRead"));
});

builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
builder.Services.AddSingleton<IWebhookSender, FakeWebhookSender>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<WebhookService>();
builder.Services.AddScoped<WebhookOutboxRunner>();
builder.Services.AddScoped<WebhookDeliverySender>();
builder.Services.AddScoped<WebhookReplayService>();
builder.Services.Configure<WebhookOutboxOptions>(builder.Configuration.GetSection("WebhookOutbox"));
builder.Services.AddHostedService<WebhookOutboxWorker>();
builder.Services.AddScoped<TaxService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ReturnService>();
builder.Services.AddScoped<ReturnEligibilityService>();
builder.Services.AddOptions<ReturnPolicyOptions>()
    .Bind(builder.Configuration.GetSection(ReturnPolicyOptions.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddScoped<FulfillmentService>();
builder.Services.AddScoped<InventoryAdjustmentService>();
builder.Services.AddScoped<OrderReorderService>();
builder.Services.AddScoped<CartMergeService>();
builder.Services.AddScoped<CartTemplateService>();
builder.Services.AddScoped<CheckoutPricingService>();
builder.Services.AddScoped<CategoryTreeService>();
builder.Services.AddScoped<CategoryOptionSchemaService>();
builder.Services.AddScoped<ProductDraftService>();
builder.Services.AddScoped<CatalogImportService>();
builder.Services.AddScoped<CatalogMutationService>();
builder.Services.AddScoped<CatalogFeedService>();
builder.Services.AddScoped<PurchaseOrderService>();
builder.Services.AddScoped<InventoryCountService>();
builder.Services.AddScoped<VariantLinePricingService>();
builder.Services.AddScoped<ShippingRulesService>();
builder.Services.AddScoped<IntegrationKeyService>();
builder.Services.AddScoped<AccountExportService>();
builder.Services.AddScoped<ReportExportService>();
builder.Services.AddSingleton<ReportExportRunner>();
builder.Services.Configure<ReportExportOptions>(builder.Configuration.GetSection(ReportExportOptions.SectionName));
builder.Services.AddHostedService<ReportExportWorker>();
builder.Services.AddScoped<OrderHoldService>();
builder.Services.AddScoped<WarehouseAssignmentService>();
builder.Services.AddScoped<Agora.Api.Queries.CartResponseFactory>();

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
