using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;

namespace Agora.Tests.Unit;

public class CartCombinationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Either_active_copy_makes_the_combined_line_active(bool targetSaved, bool sourceSaved, bool resultSaved)
    {
        var id = Guid.NewGuid();
        var line = Assert.Single(CartCombinationRules.Combine([new(id, 2, targetSaved)], [new(id, 3, sourceSaved)]));
        Assert.Equal(5, line.Quantity); Assert.Equal(resultSaved, line.IsSavedForLater);
    }

    [Fact]
    public void Replacement_validates_all_lines_before_mutation_and_preserves_existing_line_identity()
    {
        var cart = new Cart(); var first = cart.AddItem(Guid.NewGuid(), 2); var second = cart.AddItem(Guid.NewGuid(), 1);
        var version = cart.Version;
        Assert.Throws<DomainException>(() => cart.ReplaceContents([new(first.ProductVariantId, 4, true), new(second.ProductVariantId, 100, false)], DateTimeOffset.UnixEpoch));
        Assert.Equal(2, first.Quantity); Assert.False(first.IsSavedForLater); Assert.Equal(version, cart.Version);
        var newId = Guid.NewGuid();
        cart.ReplaceContents([new(first.ProductVariantId, 4, true), new(newId, 3, false)], DateTimeOffset.UnixEpoch);
        Assert.Same(first, cart.Items.Single(i => i.ProductVariantId == first.ProductVariantId));
        Assert.DoesNotContain(cart.Items, i => i.Id == second.Id);
        Assert.Equal(version + 1, cart.Version); Assert.Equal(DateTimeOffset.UnixEpoch, cart.UpdatedAt);
        Assert.True(first.IsSavedForLater); Assert.Equal(4, first.Quantity);
    }
}
