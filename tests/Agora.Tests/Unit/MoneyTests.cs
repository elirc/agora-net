using Agora.Domain.Common;

namespace Agora.Tests.Unit;

public class MoneyTests
{
    [Fact]
    public void Constructor_RoundsToTwoDecimals()
    {
        var money = new Money(10.005m);

        Assert.Equal(10.01m, money.Amount);
    }

    [Fact]
    public void Constructor_NormalizesCurrencyToUppercase()
    {
        var money = new Money(1m, "usd");

        Assert.Equal("USD", money.Currency);
    }

    [Fact]
    public void Constructor_NegativeAmount_Throws()
    {
        Assert.Throws<DomainException>(() => new Money(-0.01m));
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDX")]
    public void Constructor_InvalidCurrency_Throws(string currency)
    {
        Assert.Throws<DomainException>(() => new Money(1m, currency));
    }

    [Fact]
    public void Add_SameCurrency_SumsAmounts()
    {
        var result = new Money(10.25m).Add(new Money(5.50m));

        Assert.Equal(15.75m, result.Amount);
    }

    [Fact]
    public void Add_DifferentCurrency_Throws()
    {
        Assert.Throws<DomainException>(() => new Money(1m, "USD").Add(new Money(1m, "EUR")));
    }

    [Fact]
    public void Subtract_ClampsAtZero()
    {
        var result = new Money(5m).Subtract(new Money(10m));

        Assert.Equal(0m, result.Amount);
    }

    [Fact]
    public void Multiply_ByQuantity_ScalesAmount()
    {
        var result = new Money(19.99m).Multiply(3);

        Assert.Equal(59.97m, result.Amount);
    }

    [Fact]
    public void Multiply_ByRate_RoundsResult()
    {
        var result = new Money(19.99m).Multiply(0.08m); // 1.5992

        Assert.Equal(1.60m, result.Amount);
    }
}
