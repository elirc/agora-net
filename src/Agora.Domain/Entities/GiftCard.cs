using Agora.Domain.Common;

namespace Agora.Domain.Entities;

/// <summary>
/// Stored-value gift card. Applied at checkout as tender after discounts and
/// tax (discounts -> tax -> gift card); partial application leaves a balance.
/// </summary>
public class GiftCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = GenerateCode();
    public string Currency { get; set; } = Money.DefaultCurrency;
    public decimal InitialBalance { get; set; }
    public decimal Balance { get; private set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optimistic-concurrency token: every balance mutation bumps it, so two
    /// interleaved redemptions cannot both draw from the same balance snapshot.
    /// </summary>
    public int Version { get; private set; }

    private GiftCard()
    {
        // EF Core materialization.
    }

    public GiftCard(decimal amount, string currency = Money.DefaultCurrency, DateTimeOffset? expiresAt = null)
    {
        if (amount <= 0)
        {
            throw new DomainException("Gift card amount must be positive.");
        }

        InitialBalance = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Balance = InitialBalance;
        Currency = new Money(amount, currency).Currency; // validates the code
        ExpiresAt = expiresAt;
    }

    public bool IsRedeemable(DateTimeOffset now) =>
        IsActive && Balance > 0 && (ExpiresAt is null || ExpiresAt > now);

    /// <summary>Draws tender from the card at checkout.</summary>
    public void Redeem(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("Redemption amount must be positive.");
        }

        if (amount > Balance)
        {
            throw new InvalidGiftCardException(
                $"Gift card {Code} has {Balance:0.00} remaining; cannot redeem {amount:0.00}.");
        }

        Balance -= amount;
        Version++;
    }

    /// <summary>Returns tender to the card (order refund/cancellation).</summary>
    public void Credit(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("Credit amount must be positive.");
        }

        Balance += amount;
        Version++;
    }

    private static string GenerateCode() =>
        $"GC-{Guid.NewGuid().ToString("N")[..16].ToUpperInvariant()}";
}
