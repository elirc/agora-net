using System.Security.Claims;
using System.Text.Encodings.Web;
using Agora.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Agora.Api.Auth;

public sealed class IntegrationKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IntegrationKeyService keys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "IntegrationApiKey";
    public const string CatalogPolicy = "IntegrationCatalogRead";
    public const string InventoryPolicy = "IntegrationInventoryRead";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var values = Request.Headers["X-Agora-Api-Key"];
        if (values.Count == 0) return AuthenticateResult.NoResult();
        if (values.Count != 1 || string.IsNullOrEmpty(values[0])) return AuthenticateResult.Fail("Invalid integration key.");
        var key = await keys.AuthenticateAsync(values[0]!, Context.RequestAborted);
        if (key is null) return AuthenticateResult.Fail("Invalid integration key.");
        var claims = new List<Claim> { new("integration_key_id", key.Id.ToString()) };
        claims.AddRange(key.ScopeNames().Select(scope => new Claim("scope", scope)));
        // No customer subject or role: this identity cannot inherit administrator powers.
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }
}
