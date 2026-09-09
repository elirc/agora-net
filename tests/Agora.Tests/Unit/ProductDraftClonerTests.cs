using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Domain.Services;

namespace Agora.Tests.Unit;

public class ProductDraftClonerTests
{
    [Fact]
    public void Clone_preserves_visible_image_order_including_equal_sort_positions()
    {
        var source = new Product { Name = "Source", Slug = "source" };
        source.Images.Add(new ProductImage { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), ProductId = source.Id, Url = "https://example.test/second", SortOrder = 7 });
        source.Images.Add(new ProductImage { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), ProductId = source.Id, Url = "https://example.test/first", SortOrder = 7 });
        var clone = ProductDraftCloner.Clone(source, "Clone", "clone", new Dictionary<Guid, string>());
        var ordered = clone.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToArray();
        Assert.Equal(new[] { "https://example.test/first", "https://example.test/second" }, ordered.Select(i => i.Url));
        Assert.All(ordered, i => Assert.Equal(7, i.SortOrder));
        Assert.All(ordered, i => Assert.DoesNotContain(source.Images, original => original.Id == i.Id));
    }

    [Fact]
    public void Clone_does_not_alias_source_options_or_child_collections()
    {
        var source = new Product { Name = "Source", Slug = "source", CategoryId = Guid.NewGuid(), TaxCategoryId = Guid.NewGuid() };
        var variant = new ProductVariant { ProductId = source.Id, Sku = "SOURCE", Price = new Money(10), Options = new() { ["Size"] = "M" } };
        source.Variants.Add(variant);
        source.Images.Add(new ProductImage { ProductId = source.Id, Url = "https://example.test/source" });
        var clone = ProductDraftCloner.Clone(source, "Clone", "clone", new Dictionary<Guid, string> { [variant.Id] = "CLONE" });
        Assert.Equal(source.TaxCategoryId, clone.TaxCategoryId);
        clone.Variants[0].Options["Size"] = "L";
        clone.Images[0].Url = "https://example.test/changed";
        clone.Variants.Clear();
        Assert.Equal("M", variant.Options["Size"]);
        Assert.Equal("https://example.test/source", source.Images[0].Url);
        Assert.Single(source.Variants);
    }
}
