using System.Text;
using Agora.Domain.Entities;
using Agora.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Agora.Api.Auth;

/// <summary>Issues HMAC-SHA256 bearer tokens carrying sub/email/role claims.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, AuthenticationTimeProvider clock)
{
    public TokenIssue PrepareIssue()
    {
        var jwt = options.Value;
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds());
        return new TokenIssue(issuedAt, issuedAt.AddMinutes(jwt.ExpiryMinutes));
    }

    public string IssueToken(Customer customer, Guid sessionId, TokenIssue issue)
    {
        var jwt = options.Value;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            NotBefore = issue.IssuedAt.UtcDateTime,
            IssuedAt = issue.IssuedAt.UtcDateTime,
            Expires = issue.ExpiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = customer.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = customer.Email,
                ["role"] = customer.Role.ToString(),
                ["sid"] = sessionId.ToString(),
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}

public sealed record TokenIssue(DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);
