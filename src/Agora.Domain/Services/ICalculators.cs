using Agora.Domain.Common;

namespace Agora.Domain.Services;

public interface ITaxCalculator
{
    /// <summary>Computes tax on the discounted subtotal.</summary>
    Money CalculateTax(Money taxableAmount);
}

public interface IShippingCalculator
{
    /// <summary>Computes shipping from the discounted subtotal and unit count.</summary>
    Money CalculateShipping(Money discountedSubtotal, int totalQuantity);
}
