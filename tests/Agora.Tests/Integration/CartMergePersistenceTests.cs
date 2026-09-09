using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

public class CartMergePersistenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_stale_source_or_target_rolls_back_both_aggregates(bool changeSource)
    {
        var path = Path.Combine(Path.GetTempPath(), "agora-cart-merge-" + Guid.NewGuid().ToString("N") + ".db");
        AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
        try
        {
            Guid variantId, sourceId, targetId;
            await using (var seed = Context())
            {
                await seed.Database.EnsureCreatedAsync();
                var category = new Category { Name = "Merge", Slug = "merge" };
                var product = new Product { Name = "Merge product", Slug = "merge-product", CategoryId = category.Id };
                var variant = new ProductVariant { ProductId = product.Id, Name = "Variant", Sku = "MERGE", Price = new Money(10) };
                variantId = variant.Id; product.Variants.Add(variant);
                var source = new Cart(); source.AddItem(variantId, 2); sourceId = source.Id;
                var target = new Cart(); target.AddItem(variantId, 1); targetId = target.Id;
                seed.AddRange(category, product, source, target); await seed.SaveChangesAsync();
            }
            await using var stale = Context();
            var staleCarts = await stale.Carts.Include(c => c.Items).ToListAsync();
            var sourceCart = staleCarts.Single(c => c.Id == sourceId);
            var targetCart = staleCarts.Single(c => c.Id == targetId);
            await using (var winner = Context())
            {
                var changed = await winner.Carts.Include(c => c.Items).SingleAsync(c => c.Id == (changeSource ? sourceId : targetId));
                changed.AddItem(variantId, 1); await winner.SaveChangesAsync();
            }
            targetCart.ReplaceContents([new(variantId, 3, false)], DateTimeOffset.UtcNow);
            sourceCart.ReplaceContents([], DateTimeOffset.UtcNow);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
            await using var fresh = Context();
            var actual = await fresh.Carts.Include(c => c.Items).ToListAsync();
            Assert.Equal(changeSource ? 3 : 2, Assert.Single(actual.Single(c => c.Id == sourceId).Items).Quantity);
            Assert.Equal(changeSource ? 1 : 2, Assert.Single(actual.Single(c => c.Id == targetId).Items).Quantity);
        }
        finally { File.Delete(path); }
    }
}
