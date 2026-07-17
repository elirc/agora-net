using System.Text;
using Agora.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Agora.Api.Auth;

/// <summary>Issues HMAC-SHA256 bearer tokens carrying sub/email/role claims.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    public (string Token, DateTimeOffset ExpiresAt) IssueToken(Customer customer)
    {
        var jwt = options.Value;
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(jwt.ExpiryMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = customer.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = customer.Email,
                ["role"] = customer.Role.ToString(),
            },
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
