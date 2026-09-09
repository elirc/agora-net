using System.Text.Json;
using Agora.Api.Contracts;
using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class BootcampResponseContractTests
{
    private static JsonElement Json(object value) =>
        JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Theory]
    [InlineData(1, 5, false, true)]
    [InlineData(2, 5, true, true)]
    [InlineData(3, 5, true, false)]
    [InlineData(8, 5, true, false)]
    [InlineData(1, 0, false, false)]
    public void Page_navigation_describes_requested_page(int page, int count, bool previous, bool next)
    {
        var json = Json(new PagedResult<int>([], page, 2, count));
        Assert.Equal(previous, json.GetProperty("hasPreviousPage").GetBoolean());
        Assert.Equal(next, json.GetProperty("hasNextPage").GetBoolean());
    }

    [Fact]
    public void Cart_line_counts_are_not_quantities()
    {
        var line = new CartItemResponse(Guid.NewGuid(), Guid.NewGuid(), "A", "Tee", "Blue", 3,
            new MoneyDto(10, "USD"), new MoneyDto(30, "USD"));
        var cart = new CartResponse("token", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [line, line with { Id = Guid.NewGuid(), Quantity = 2 }],
            [line with { Quantity = 4 }], 5, new MoneyDto(50, "USD"));
        var json = Json(cart);
        Assert.Equal(2, json.GetProperty("activeLineCount").GetInt32());
        Assert.Equal(1, json.GetProperty("savedLineCount").GetInt32());
        Assert.Equal(5, json.GetProperty("totalQuantity").GetInt32());
    }

    [Theory]
    [InlineData(5, 0, true)]
    [InlineData(5, 5, false)]
    [InlineData(0, 0, false)]
    [InlineData(5, 4, true)]
    public void Availability_uses_unreserved_units(int onHand, int reserved, bool expected)
    {
        var inventory = new InventoryItem(Guid.NewGuid(), onHand);
        if (reserved > 0) inventory.Reserve(reserved);
        var json = Json(InventoryResponse.From("A", inventory));
        Assert.Equal(expected, json.GetProperty("inStock").GetBoolean());
    }

    [Fact]
    public void Product_mapping_has_stable_choices_weight_count_and_primary_image()
    {
        var firstImage = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var product = new Product
        {
            Name = "Example",
            Variants =
            [
                new ProductVariant { Sku = "z", Price = new Money(5), WeightGrams = 250 },
                new ProductVariant { Sku = "A", Price = new Money(8), WeightGrams = 100 },
            ],
            Images =
            [
                new ProductImage { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Url = "https://example.test/b", SortOrder = 0 },
                new ProductImage { Id = firstImage, Url = "https://example.test/a", SortOrder = 0 },
            ],
        };
        var response = ProductResponse.From(product);
        Assert.Equal(new[] { "A", "z" }, response.Variants.Select(v => v.Sku));
        var json = Json(response);
        Assert.Equal(2, json.GetProperty("variantCount").GetInt32());
        Assert.Equal(100, json.GetProperty("variants")[0].GetProperty("weightGrams").GetInt32());
        Assert.Equal(firstImage, json.GetProperty("primaryImage").GetProperty("id").GetGuid());
        Assert.Equal(firstImage, response.Images[0].Id);
    }

    [Fact]
    public void Product_without_images_has_null_primary_image()
    {
        var json = Json(ProductResponse.From(new Product()));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("primaryImage").ValueKind);
        Assert.Equal(0, json.GetProperty("variantCount").GetInt32());
    }

    [Fact]
    public void Wishlist_counts_follow_mapped_stock_flags()
    {
        var item = new WishlistItemResponse(Guid.NewGuid(), Guid.NewGuid(), "A", "Tee", "Blue",
            new MoneyDto(10, "USD"), true, false, DateTimeOffset.UtcNow);
        var json = Json(new WishlistResponse(Guid.NewGuid(), "Gifts", false,
            [item, item with { InStock = false }, item], DateTimeOffset.UtcNow));
        Assert.Equal(2, json.GetProperty("inStockItemCount").GetInt32());
        Assert.Equal(1, json.GetProperty("outOfStockItemCount").GetInt32());
    }
}
