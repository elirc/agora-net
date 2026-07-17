using Agora.Domain.Common;
using Agora.Infrastructure.Services;

namespace Agora.Tests.Unit;

public class FakePaymentGatewayTests
{
    private readonly FakePaymentGateway _gateway = new();

    [Fact]
    public async Task Charge_WithValidToken_Succeeds()
    {
        var result = await _gateway.ChargeAsync("ORD-1", new Money(10m), "tok_visa");

        Assert.True(result.Success);
        Assert.StartsWith("txn_", result.TransactionId);
    }

    [Theory]
    [InlineData(FakePaymentGateway.DeclineToken)]
    [InlineData("fail_any")]
    [InlineData("")]
    public async Task Charge_WithBadToken_Fails(string token)
    {
        var result = await _gateway.ChargeAsync("ORD-1", new Money(10m), token);

        Assert.False(result.Success);
        Assert.Null(result.TransactionId);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task Refund_WithTransactionId_Succeeds()
    {
        var result = await _gateway.RefundAsync("txn_abc", new Money(10m));

        Assert.True(result.Success);
        Assert.StartsWith("rfnd_", result.TransactionId);
    }
}
