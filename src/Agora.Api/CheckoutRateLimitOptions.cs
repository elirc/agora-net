namespace Agora.Api;

/// <summary>Fixed-window rate limit applied to checkout, bound from configuration.</summary>
public sealed class CheckoutRateLimitOptions
{
    public const string SectionName = "RateLimiting:Checkout";

    /// <summary>Checkout attempts allowed per client per window.</summary>
    public int PermitLimit { get; set; } = 10;

    public int WindowSeconds { get; set; } = 60;
}
