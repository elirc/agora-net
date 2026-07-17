using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Agora.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated customer's id, or null for anonymous requests.</summary>
    public static Guid? GetCustomerId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
}
