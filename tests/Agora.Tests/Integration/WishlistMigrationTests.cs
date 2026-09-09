using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Agora.Tests.Integration;

public class WishlistMigrationTests
{
    [Fact]
    public async Task Existing_wishlist_rows_survive_upgrade_with_empty_notes_and_zero_revisions()
    {
        var path = Path.Combine(Path.GetTempPath(), "agora-upgrade-" + Guid.NewGuid().ToString("N") + ".db");
        var options = new DbContextOptionsBuilder<AgoraDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        try
        {
            Guid listId;
            Guid itemId;
            await using (var db = new AgoraDbContext(options))
            {
                await db.Database.MigrateAsync();
                var customer = new Customer { Email = "migration@test.local", FullName = "Old owner" };
                var category = new Category { Name = "Old category", Slug = "old-category" };
                var product = new Product { CategoryId = category.Id, Name = "Old product", Slug = "old-product" };
                var variant = new ProductVariant { ProductId = product.Id, Sku = "OLD-SKU", Name = "Old choice", Price = new Money(12) };
                product.Variants.Add(variant);
                var list = new Wishlist { CustomerId = customer.Id, Name = "Old list" };
                var item = list.AddItem(variant.Id, true);
                db.AddRange(customer, category, product, list);
                await db.SaveChangesAsync();
                listId = list.Id;
                itemId = item.Id;
                // Recreate the preceding physical schema with real rows. The downgrade removes only the new fields.
                await db.GetService<IMigrator>().MigrateAsync("20260717063957_GiftCardConcurrency");
            }
            await using (var old = new AgoraDbContext(options))
            {
                await old.Database.OpenConnectionAsync();
                await using var command = old.Database.GetDbConnection().CreateCommand();
                command.CommandText = "PRAGMA table_info('WishlistItems')";
                await using var reader = await command.ExecuteReaderAsync();
                var names = new List<string>();
                while (await reader.ReadAsync()) names.Add(reader.GetString(1));
                Assert.DoesNotContain("Note", names);
                Assert.DoesNotContain("NoteVersion", names);
            }
            await using (var upgraded = new AgoraDbContext(options))
            {
                await upgraded.Database.MigrateAsync();
                Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
                var list = await upgraded.Wishlists.Include(w => w.Items).SingleAsync(w => w.Id == listId);
                var item = Assert.Single(list.Items);
                Assert.Equal(itemId, item.Id);
                Assert.Equal("Old list", list.Name);
                Assert.True(item.OutOfStockObserved);
                Assert.Null(item.Note);
                Assert.Equal(0, item.NoteVersion);
                Assert.Equal(0, list.MembershipVersion);
                item.EditNote("After upgrade");
                await upgraded.SaveChangesAsync();
            }
        }
        finally { File.Delete(path); }
    }
}
