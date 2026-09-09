namespace Agora.Api.Contracts;

public sealed record LoginSessionResponse(
    Guid Id,
    string? DeviceLabel,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    bool IsCurrent);

public sealed record RevokeAllSessionsResponse(int RevokedCount, DateTimeOffset RevokedAt);
