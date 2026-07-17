using Agora.Domain.Common;

namespace Agora.Domain.Entities;

/// <summary>
/// Stock record for a single variant. Checkout reserves stock first
/// (<see cref="Reserve"/>), then either commits the reservation on successful
/// payment (<see cref="CommitReservation"/>) or releases it on failure
/// (<see cref="ReleaseReservation"/>).
/// </summary>
public class InventoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public int QuantityOnHand { get; private set; }
    public int QuantityReserved { get; private set; }
    public int QuantityAvailable => QuantityOnHand - QuantityReserved;

    /// <summary>
    /// Optimistic-concurrency token: every stock mutation bumps it, so two
    /// interleaved writers cannot both commit against the same snapshot.
    /// </summary>
    public int Version { get; private set; }

    private InventoryItem()
    {
        // EF Core materialization.
    }

    public InventoryItem(Guid productVariantId, int quantityOnHand)
    {
        if (quantityOnHand < 0)
        {
            throw new DomainException("Quantity on hand cannot be negative.");
        }

        ProductVariantId = productVariantId;
        QuantityOnHand = quantityOnHand;
    }

    /// <summary>Sets the absolute on-hand quantity (stock take / restock).</summary>
    public void SetStock(int quantityOnHand)
    {
        if (quantityOnHand < 0)
        {
            throw new DomainException("Quantity on hand cannot be negative.");
        }

        if (quantityOnHand < QuantityReserved)
        {
            throw new DomainException(
                $"Cannot set stock to {quantityOnHand}: {QuantityReserved} unit(s) are currently reserved.");
        }

        QuantityOnHand = quantityOnHand;
        Version++;
    }

    public void Reserve(int quantity)
    {
        EnsurePositive(quantity);
        if (QuantityAvailable < quantity)
        {
            throw new InsufficientStockException(
                $"Insufficient stock: requested {quantity}, available {QuantityAvailable}.");
        }

        QuantityReserved += quantity;
        Version++;
    }

    public void ReleaseReservation(int quantity)
    {
        EnsurePositive(quantity);
        QuantityReserved = Math.Max(0, QuantityReserved - quantity);
        Version++;
    }

    /// <summary>Converts a reservation into a real deduction (payment succeeded).</summary>
    public void CommitReservation(int quantity)
    {
        EnsurePositive(quantity);
        if (quantity > QuantityReserved)
        {
            throw new DomainException(
                $"Cannot commit {quantity} unit(s): only {QuantityReserved} reserved.");
        }

        QuantityReserved -= quantity;
        QuantityOnHand -= quantity;
        Version++;
    }

    /// <summary>Returns units to stock (cancellation/refund restock).</summary>
    public void Restock(int quantity)
    {
        EnsurePositive(quantity);
        QuantityOnHand += quantity;
        Version++;
    }

    private static void EnsurePositive(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be positive.");
        }
    }
}
