namespace Agora.Infrastructure.Services;

/// <summary>
/// Security clock kept separate from the application/business clock. Tests that
/// move order or reporting time by months must not accidentally age login tokens.
/// Authentication-focused tests can replace this service with a controlled clock.
/// </summary>
public class AuthenticationTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
}
