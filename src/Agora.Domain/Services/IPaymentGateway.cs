using Agora.Domain.Common;

namespace Agora.Domain.Services;

public sealed record PaymentResult(bool Success, string? TransactionId, string? FailureReason)
{
    public static PaymentResult Succeeded(string transactionId) => new(true, transactionId, null);
    public static PaymentResult Failed(string reason) => new(false, null, reason);
}

/// <summary>Payment provider abstraction; production would wrap Stripe et al.</summary>
public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(string orderNumber, Money amount, string paymentToken, CancellationToken ct = default);

    Task<PaymentResult> RefundAsync(string transactionId, Money amount, CancellationToken ct = default);
}
