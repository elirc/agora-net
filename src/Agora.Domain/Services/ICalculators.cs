using Agora.Domain.Common;

namespace Agora.Domain.Services;

public interface ITaxCalculator
{
    /// <summary>Computes tax on the discounted subtotal.</summary>
    Money CalculateTax(Money taxableAmount);
}
