using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class CartTests
{
    private readonly Cart _cart = new();
    private readonly Guid _variantId = Guid.NewGuid();

    [Fact]
    public void AddItem_NewVariant_AddsLine()
    {
        var item = _cart.AddItem(_variantId, 2);

        Assert.Single(_cart.Items);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(_variantId, item.ProductVariantId);
    }

    [Fact]
    public void AddItem_ExistingVariant_MergesQuantity()
    {
        _cart.AddItem(_variantId, 2);
        _cart.AddItem(_variantId, 3);

        Assert.Single(_cart.Items);
        Assert.Equal(5, _cart.Items[0].Quantity);
    }

    [Fact]
    public void AddItem_ZeroQuantity_Throws()
    {
        Assert.Throws<DomainException>(() => _cart.AddItem(_variantId, 0));
    }

    [Fact]
    public void AddItem_OverMaxQuantity_Throws()
    {
        Assert.Throws<DomainException>(() => _cart.AddItem(_variantId, Cart.MaxQuantityPerLine + 1));
    }

    [Fact]
    public void AddItem_MergeExceedingMax_Throws()
    {
        _cart.AddItem(_variantId, 60);

        Assert.Throws<DomainException>(() => _cart.AddItem(_variantId, 60));
    }

    [Fact]
    public void UpdateItemQuantity_SetsNewQuantity()
    {
        var item = _cart.AddItem(_variantId, 2);

        _cart.UpdateItemQuantity(item.Id, 7);

        Assert.Equal(7, _cart.Items[0].Quantity);
    }

    [Fact]
    public void UpdateItemQuantity_Zero_RemovesLine()
    {
        var item = _cart.AddItem(_variantId, 2);

        _cart.UpdateItemQuantity(item.Id, 0);

        Assert.Empty(_cart.Items);
    }

    [Fact]
    public void UpdateItemQuantity_UnknownItem_ThrowsNotFound()
    {
        Assert.Throws<NotFoundException>(() => _cart.UpdateItemQuantity(Guid.NewGuid(), 1));
    }

    [Fact]
    public void RemoveItem_RemovesLine()
    {
        var item = _cart.AddItem(_variantId, 1);

        _cart.RemoveItem(item.Id);

        Assert.Empty(_cart.Items);
    }

    [Fact]
    public void Mutations_TouchUpdatedAt()
    {
        var before = _cart.UpdatedAt;

        _cart.AddItem(_variantId, 1);

        Assert.True(_cart.UpdatedAt >= before);
    }
}
