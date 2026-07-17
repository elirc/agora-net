using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class InventoryItemTests
{
    private readonly InventoryItem _item = new(Guid.NewGuid(), 10);

    [Fact]
    public void Reserve_ReducesAvailableNotOnHand()
    {
        _item.Reserve(4);

        Assert.Equal(10, _item.QuantityOnHand);
        Assert.Equal(4, _item.QuantityReserved);
        Assert.Equal(6, _item.QuantityAvailable);
    }

    [Fact]
    public void Reserve_MoreThanAvailable_Throws()
    {
        _item.Reserve(8);

        Assert.Throws<InsufficientStockException>(() => _item.Reserve(3));
    }

    [Fact]
    public void ReleaseReservation_RestoresAvailability()
    {
        _item.Reserve(5);
        _item.ReleaseReservation(5);

        Assert.Equal(0, _item.QuantityReserved);
        Assert.Equal(10, _item.QuantityAvailable);
    }

    [Fact]
    public void CommitReservation_DeductsOnHandAndReserved()
    {
        _item.Reserve(3);
        _item.CommitReservation(3);

        Assert.Equal(7, _item.QuantityOnHand);
        Assert.Equal(0, _item.QuantityReserved);
    }

    [Fact]
    public void CommitReservation_MoreThanReserved_Throws()
    {
        _item.Reserve(2);

        Assert.Throws<DomainException>(() => _item.CommitReservation(3));
    }

    [Fact]
    public void SetStock_BelowReserved_Throws()
    {
        _item.Reserve(6);

        Assert.Throws<DomainException>(() => _item.SetStock(5));
    }

    [Fact]
    public void SetStock_Negative_Throws()
    {
        Assert.Throws<DomainException>(() => _item.SetStock(-1));
    }

    [Fact]
    public void Restock_IncreasesOnHand()
    {
        _item.Restock(5);

        Assert.Equal(15, _item.QuantityOnHand);
    }

    [Fact]
    public void Reserve_NonPositiveQuantity_Throws()
    {
        Assert.Throws<DomainException>(() => _item.Reserve(0));
    }
}
