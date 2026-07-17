namespace Agora.Domain.Common;

/// <summary>Base exception for domain rule violations. Maps to HTTP 400/409 at the API edge.</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Thrown when a requested aggregate does not exist. Maps to HTTP 404.</summary>
public sealed class NotFoundException(string message) : DomainException(message);

/// <summary>Thrown when a stock operation cannot be satisfied by available inventory.</summary>
public sealed class InsufficientStockException(string message) : DomainException(message);

/// <summary>Thrown on an illegal order lifecycle transition.</summary>
public sealed class InvalidOrderStateException(string message) : DomainException(message);

/// <summary>Thrown when a discount code cannot be applied.</summary>
public sealed class InvalidDiscountException(string message) : DomainException(message);

/// <summary>Thrown when a fulfillment cannot be created as specified.</summary>
public sealed class InvalidFulfillmentException(string message) : DomainException(message);

/// <summary>Thrown on an illegal return (RMA) lifecycle transition.</summary>
public sealed class InvalidReturnStateException(string message) : DomainException(message);

/// <summary>Thrown when a return request cannot be created as specified.</summary>
public sealed class InvalidReturnRequestException(string message) : DomainException(message);

/// <summary>Thrown when a shipping method cannot be used at checkout.</summary>
public sealed class InvalidShippingMethodException(string message) : DomainException(message);

/// <summary>Thrown when a gift card cannot be applied at checkout.</summary>
public sealed class InvalidGiftCardException(string message) : DomainException(message);

/// <summary>Thrown when the payment gateway declines a charge. Maps to HTTP 402.</summary>
public sealed class PaymentFailedException(string message) : DomainException(message);
