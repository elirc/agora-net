namespace Agora.Api.Auth;

/// <summary>JWT issuing/validation settings, bound from the "Jwt" configuration section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "agora-api";
    public string Audience { get; set; } = "agora-clients";

    /// <summary>Symmetric HMAC-SHA256 signing key; must be at least 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}
