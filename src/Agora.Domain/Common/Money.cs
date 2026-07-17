namespace Agora.Domain.Common;

/// <summary>
/// Decimal amount plus ISO 4217 currency code. Amounts are rounded to 2 decimal
/// places and can never be negative; subtraction clamps at zero (useful when a
/// discount exceeds the subtotal).
/// </summary>
public sealed record Money
{
    public const string DefaultCurrency = "USD";

    public decimal Amount { get; init; }
    public string Currency { get; init; } = DefaultCurrency;

    private Money()
    {
        // EF Core materialization.
    }

    public Money(decimal amount, string currency = DefaultCurrency)
    {
        if (amount < 0)
        {
            throw new DomainException("Money amount cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new DomainException("Currency must be a 3-letter ISO code.");
        }

        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency.Trim().ToUpperInvariant();
    }

    public static Money Zero(string currency = DefaultCurrency) => new(0m, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>Subtracts, clamping at zero rather than going negative.</summary>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        var result = Amount - other.Amount;
        return new Money(result < 0 ? 0 : result, Currency);
    }

    public Money Multiply(int factor) => new(Amount * factor, Currency);

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new DomainException($"Currency mismatch: {Currency} vs {other.Currency}.");
        }
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
