using Agora.Domain.Common;
using Agora.Domain.Services;

namespace Agora.Infrastructure.Services;

/// <summary>
/// Deterministic in-memory payment gateway for development and tests.
/// Any payment token equal to <see cref="DeclineToken"/> (or prefixed "fail")
/// is declined; everything else is charged successfully.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public const string DeclineToken = "tok_fail";

    public Task<PaymentResult> ChargeAsync(
        string orderNumber, Money amount, string paymentToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paymentToken))
        {
            return Task.FromResult(PaymentResult.Failed("missing_payment_token"));
        }

        if (paymentToken == DeclineToken || paymentToken.StartsWith("fail", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(PaymentResult.Failed("card_declined"));
        }

        return Task.FromResult(PaymentResult.Succeeded($"txn_{Guid.NewGuid():N}"));
    }

    public Task<PaymentResult> RefundAsync(string transactionId, Money amount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return Task.FromResult(PaymentResult.Failed("unknown_transaction"));
        }

        return Task.FromResult(PaymentResult.Succeeded($"rfnd_{Guid.NewGuid():N}"));
    }
}
