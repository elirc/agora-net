using Agora.Domain.Common;

namespace Agora.Domain.Entities;

public enum DiscountType
{
    Percentage = 1,
    FixedAmount = 2,
}

public class DiscountCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public DiscountType Type { get; set; }

    /// <summary>Percent (0-100) for <see cref="DiscountType.Percentage"/>, otherwise a fixed amount.</summary>
    public decimal Value { get; set; }

    public string Currency { get; set; } = Money.DefaultCurrency;
    public DateTimeOffset? ExpiresAt { get; set; }
    public int? UsageLimit { get; set; }
    public int TimesUsed { get; set; }
    public bool IsActive { get; set; } = true;

    public bool IsRedeemable(DateTimeOffset now) =>
        IsActive
        && (ExpiresAt is null || ExpiresAt > now)
        && (UsageLimit is null || TimesUsed < UsageLimit);

    public Money CalculateDiscount(Money subtotal) => Type switch
    {
        DiscountType.Percentage => subtotal.Multiply(Value / 100m),
        DiscountType.FixedAmount => new Money(Math.Min(Value, subtotal.Amount), subtotal.Currency),
        _ => Money.Zero(subtotal.Currency),
    };

    public void RegisterUse(DateTimeOffset now)
    {
        if (!IsRedeemable(now))
        {
            throw new InvalidDiscountException($"Discount code '{Code}' is not redeemable.");
        }

        TimesUsed++;
    }
}
