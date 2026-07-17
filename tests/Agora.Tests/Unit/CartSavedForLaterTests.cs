using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class CartSavedForLaterTests
{
    [Fact]
    public void SaveForLater_RemovesLineFromActiveItems()
    {
        var cart = new Cart();
        var item = cart.AddItem(Guid.NewGuid(), 2);

        cart.SaveForLater(item.Id);

        Assert.True(item.IsSavedForLater);
        Assert.Empty(cart.ActiveItems);
        Assert.Single(cart.Items);
    }

    [Fact]
    public void ActivateItem_RestoresLine()
    {
        var cart = new Cart();
        var item = cart.AddItem(Guid.NewGuid(), 2);
        cart.SaveForLater(item.Id);

        cart.ActivateItem(item.Id);

        Assert.False(item.IsSavedForLater);
        Assert.Single(cart.ActiveItems);
    }

    [Fact]
    public void AddItem_SameVariantAsSavedLine_ReactivatesAndMerges()
    {
        var cart = new Cart();
        var variantId = Guid.NewGuid();
        var item = cart.AddItem(variantId, 2);
        cart.SaveForLater(item.Id);

        var merged = cart.AddItem(variantId, 3);

        Assert.Same(item, merged);
        Assert.False(merged.IsSavedForLater);
        Assert.Equal(5, merged.Quantity);
        Assert.Single(cart.Items);
    }

    [Fact]
    public void RemoveActiveItems_KeepsSavedLines()
    {
        var cart = new Cart();
        var active = cart.AddItem(Guid.NewGuid(), 1);
        var saved = cart.AddItem(Guid.NewGuid(), 2);
        cart.SaveForLater(saved.Id);

        cart.RemoveActiveItems();

        var remaining = Assert.Single(cart.Items);
        Assert.Equal(saved.Id, remaining.Id);
        Assert.DoesNotContain(cart.Items, i => i.Id == active.Id);
    }
}
