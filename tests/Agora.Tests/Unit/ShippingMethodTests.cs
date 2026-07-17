using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class ShippingMethodTests
{
    private static ShippingMethod Flat(decimal baseRate, decimal? freeThreshold = null) => new()
    {
        Code = "flat",
        Name = "Flat",
        RateType = ShippingRateType.Flat,
        BaseRate = baseRate,
        FreeThreshold = freeThreshold,
    };

    private static ShippingMethod Weighted(decimal baseRate, decimal perKg, decimal? freeThreshold = null) => new()
    {
        Code = "weighted",
        Name = "Weighted",
        RateType = ShippingRateType.Weighted,
        BaseRate = baseRate,
        PerKgRate = perKg,
        FreeThreshold = freeThreshold,
    };

    [Fact]
    public void Flat_ChargesBaseRate()
    {
        var charge = Flat(5.99m).CalculateCharge(new Money(20m), 500);

        Assert.Equal(5.99m, charge.Amount);
    }

    [Fact]
    public void Flat_AtFreeThreshold_IsFree()
    {
        var charge = Flat(5.99m, freeThreshold: 50m).CalculateCharge(new Money(50m), 500);

        Assert.Equal(0m, charge.Amount);
    }

    [Fact]
    public void Flat_JustUnderFreeThreshold_Charges()
    {
        var charge = Flat(5.99m, freeThreshold: 50m).CalculateCharge(new Money(49.99m), 500);

        Assert.Equal(5.99m, charge.Amount);
    }

    [Fact]
    public void Weighted_AddsPerKilogramRate()
    {
        // 4.99 base + 2.00/kg * 1.5kg = 7.99
        var charge = Weighted(4.99m, 2.00m).CalculateCharge(new Money(20m), 1500);

        Assert.Equal(7.99m, charge.Amount);
    }

    [Fact]
    public void Weighted_RoundsFractionalGrams()
    {
        // 2.00/kg * 0.333kg = 0.666 -> 0.67; 4.99 + 0.67 = 5.66
        var charge = Weighted(4.99m, 2.00m).CalculateCharge(new Money(20m), 333);

        Assert.Equal(5.66m, charge.Amount);
    }

    [Fact]
    public void Weighted_ZeroWeight_ChargesBaseOnly()
    {
        var charge = Weighted(4.99m, 2.00m).CalculateCharge(new Money(20m), 0);

        Assert.Equal(4.99m, charge.Amount);
    }

    [Fact]
    public void Weighted_OverFreeThreshold_IsFree()
    {
        var charge = Weighted(4.99m, 2.00m, freeThreshold: 100m)
            .CalculateCharge(new Money(120m), 5000);

        Assert.Equal(0m, charge.Amount);
    }

    [Fact]
    public void Charge_PreservesCurrency()
    {
        var charge = Flat(5.99m).CalculateCharge(new Money(10m, "EUR"), 100);

        Assert.Equal("EUR", charge.Currency);
    }

    [Fact]
    public void NegativeWeight_Throws()
    {
        Assert.Throws<DomainException>(() =>
            Weighted(4.99m, 2.00m).CalculateCharge(new Money(10m), -1));
    }
}
