using Agora.Domain.Common;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agora.Infrastructure.Persistence;

/// <summary>Seeds a small catalog for local development. Idempotent: skips when data exists.</summary>
public static class AgoraDbSeeder
{
    public static async Task SeedAsync(AgoraDbContext db, CancellationToken ct = default)
    {
        if (await db.Categories.AnyAsync(ct))
        {
            return;
        }

        var apparel = new Category { Name = "Apparel", Slug = "apparel", Description = "Clothing and accessories" };
        var electronics = new Category { Name = "Electronics", Slug = "electronics", Description = "Gadgets and devices" };
        var home = new Category { Name = "Home & Kitchen", Slug = "home-kitchen", Description = "Everything for the home" };
        db.Categories.AddRange(apparel, electronics, home);

        var products = new List<Product>
        {
            NewProduct(apparel, "Classic Cotton Tee", "classic-cotton-tee",
                "A soft, breathable everyday t-shirt in 100% organic cotton.",
                [
                    ("TEE-BLK-S", "Black / S", 19.99m, new() { ["Color"] = "Black", ["Size"] = "S" }, 40),
                    ("TEE-BLK-M", "Black / M", 19.99m, new() { ["Color"] = "Black", ["Size"] = "M" }, 55),
                    ("TEE-WHT-M", "White / M", 19.99m, new() { ["Color"] = "White", ["Size"] = "M" }, 30),
                ]),
            NewProduct(apparel, "Trailblazer Hoodie", "trailblazer-hoodie",
                "Heavyweight fleece hoodie with kangaroo pocket.",
                [
                    ("HOOD-GRY-M", "Grey / M", 54.50m, new() { ["Color"] = "Grey", ["Size"] = "M" }, 20),
                    ("HOOD-GRY-L", "Grey / L", 54.50m, new() { ["Color"] = "Grey", ["Size"] = "L" }, 18),
                ]),
            NewProduct(apparel, "Canvas Weekender Cap", "canvas-weekender-cap",
                "Adjustable six-panel cap in washed canvas.",
                [
                    ("CAP-KHK", "Khaki", 24.00m, new() { ["Color"] = "Khaki" }, 60),
                ]),
            NewProduct(electronics, "Aurora Wireless Earbuds", "aurora-wireless-earbuds",
                "Active noise cancelling earbuds with 30-hour battery case.",
                [
                    ("EAR-AUR-BLK", "Black", 129.99m, new() { ["Color"] = "Black" }, 25),
                    ("EAR-AUR-WHT", "White", 129.99m, new() { ["Color"] = "White" }, 15),
                ]),
            NewProduct(electronics, "Volt 65W GaN Charger", "volt-65w-gan-charger",
                "Compact dual-port USB-C fast charger.",
                [
                    ("CHG-65W", "Standard", 44.95m, [], 80),
                ]),
            NewProduct(electronics, "Nimbus Mechanical Keyboard", "nimbus-mechanical-keyboard",
                "Hot-swappable 75% keyboard with RGB backlight.",
                [
                    ("KB-NIM-BRN", "Brown switches", 89.00m, new() { ["Switch"] = "Brown" }, 12),
                    ("KB-NIM-RED", "Red switches", 89.00m, new() { ["Switch"] = "Red" }, 9),
                ]),
            NewProduct(home, "Ember Pour-Over Kettle", "ember-pour-over-kettle",
                "Gooseneck kettle with built-in thermometer, 1L.",
                [
                    ("KET-EMB-1L", "1 Litre", 39.99m, [], 35),
                ]),
            NewProduct(home, "Cedar Scented Candle", "cedar-scented-candle",
                "Hand-poured soy candle, 45-hour burn time.",
                [
                    ("CDL-CDR-S", "Small (20cl)", 14.50m, new() { ["Size"] = "Small" }, 100),
                    ("CDL-CDR-L", "Large (40cl)", 26.00m, new() { ["Size"] = "Large" }, 0),
                ]),
        };
        db.Products.AddRange(products);

        db.DiscountCodes.AddRange(
            new DiscountCode
            {
                Code = "WELCOME10",
                Type = DiscountType.Percentage,
                Value = 10m,
                UsageLimit = 100,
            },
            new DiscountCode
            {
                Code = "SAVE5",
                Type = DiscountType.FixedAmount,
                Value = 5.00m,
                ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
            },
            new DiscountCode
            {
                Code = "EXPIRED10",
                Type = DiscountType.Percentage,
                Value = 10m,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            });

        await db.SaveChangesAsync(ct);
    }

    private static Product NewProduct(
        Category category,
        string name,
        string slug,
        string description,
        List<(string Sku, string VariantName, decimal Price, Dictionary<string, string> Options, int Stock)> variants)
    {
        var product = new Product
        {
            Category = category,
            CategoryId = category.Id,
            Name = name,
            Slug = slug,
            Description = description,
        };

        foreach (var (sku, variantName, price, options, stock) in variants)
        {
            var variant = new ProductVariant
            {
                ProductId = product.Id,
                Sku = sku,
                Name = variantName,
                Price = new Money(price),
                Options = options,
            };
            variant.Inventory = new InventoryItem(variant.Id, stock);
            product.Variants.Add(variant);
        }

        product.Images.Add(new ProductImage
        {
            ProductId = product.Id,
            Url = $"https://images.agora.example/{slug}/main.jpg",
            AltText = name,
            SortOrder = 0,
        });

        return product;
    }
}
