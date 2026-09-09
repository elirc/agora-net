using Agora.Domain.Common;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agora.Tests.Integration;

/// <summary>Independent SQLite connections prove persisted conflict checks, not just request comparisons.</summary>
public class WishlistConcurrencyTests
{
    [Fact]
    public async Task Stale_note_and_stock_observation_cannot_overwrite_a_newer_note()
    {
        await using var store = await Store.Create();
        await using var first = store.Context();
        await using var second = store.Context();
        await using var observer = store.Context();
        var a = await first.WishlistItems.SingleAsync();
        var b = await second.WishlistItems.SingleAsync();
        var staleObservation = await observer.WishlistItems.SingleAsync();
        b.EditNote("newer note");
        await second.SaveChangesAsync();
        a.EditNote("stale note");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => first.SaveChangesAsync());
        staleObservation.OutOfStockObserved = true;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => observer.SaveChangesAsync());
        await using var fresh = store.Context();
        var actual = await fresh.WishlistItems.SingleAsync();
        Assert.Equal("newer note", actual.Note);
        Assert.Equal(1, actual.NoteVersion);
        Assert.False(actual.OutOfStockObserved);
    }

    [Fact]
    public async Task Note_save_does_not_erase_a_stock_observation_saved_after_it_was_loaded()
    {
        await using var store = await Store.Create();
        await using var editor = store.Context();
        await using var observer = store.Context();
        var note = await editor.WishlistItems.SingleAsync();
        var stock = await observer.WishlistItems.SingleAsync();
        stock.OutOfStockObserved = true;
        await observer.SaveChangesAsync();
        note.EditNote("gift");
        await editor.SaveChangesAsync();
        await using var fresh = store.Context();
        var actual = await fresh.WishlistItems.SingleAsync();
        Assert.True(actual.OutOfStockObserved);
        Assert.Equal("gift", actual.Note);
    }

    [Fact]
    public async Task Stale_membership_save_rolls_back_new_children_and_cannot_delete_the_parent()
    {
        await using var store = await Store.Create();
        await using var first = store.Context();
        await using var second = store.Context();
        await using var deleting = store.Context();
        var stale = await first.Wishlists.Include(w => w.Items).SingleAsync();
        var current = await second.Wishlists.Include(w => w.Items).SingleAsync();
        var staleDelete = await deleting.Wishlists.SingleAsync();
        var choices = await first.ProductVariants.OrderBy(v => v.Sku).ToListAsync();
        second.WishlistItems.Add(current.AddItem(choices[1].Id, false));
        await second.SaveChangesAsync();
        first.WishlistItems.Add(stale.AddItem(choices[2].Id, false));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => first.SaveChangesAsync());
        deleting.Wishlists.Remove(staleDelete);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => deleting.SaveChangesAsync());
        await using var fresh = store.Context();
        var result = await fresh.Wishlists.Include(w => w.Items).SingleAsync();
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, i => i.ProductVariantId == choices[1].Id);
        Assert.DoesNotContain(result.Items, i => i.ProductVariantId == choices[2].Id);
        Assert.Equal(2, result.MembershipVersion);
    }

    [Fact]
    public async Task Two_copies_of_same_variant_cannot_create_duplicates_or_advance_losing_revision()
    {
        await using var store = await Store.Create();
        await using var first = store.Context();
        await using var second = store.Context();
        var a = await first.Wishlists.Include(w => w.Items).SingleAsync();
        var b = await second.Wishlists.Include(w => w.Items).SingleAsync();
        var variant = await first.ProductVariants.OrderBy(v => v.Sku).Skip(1).FirstAsync();
        first.WishlistItems.Add(a.AddItem(variant.Id, false));
        second.WishlistItems.Add(b.AddItem(variant.Id, false));
        await first.SaveChangesAsync();
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => second.SaveChangesAsync());
        await using var fresh = store.Context();
        var result = await fresh.Wishlists.Include(w => w.Items).SingleAsync();
        Assert.Single(result.Items, i => i.ProductVariantId == variant.Id);
        Assert.Equal(2, result.MembershipVersion);
    }

    [Fact]
    public async Task All_skipped_copy_still_checks_the_parent_revision_without_advancing_it()
    {
        await using var store = await Store.Create();
        await using var noOp = store.Context();
        await using var writer = store.Context();
        var observed = await noOp.Wishlists.SingleAsync();
        var changed = await writer.Wishlists.Include(w => w.Items).SingleAsync();
        var variant = await writer.ProductVariants.OrderBy(v => v.Sku).Skip(1).FirstAsync();
        // This is the no-op persistence path used after CopyItems finds every choice already present.
        noOp.Entry(observed).Property(w => w.MembershipVersion).IsModified = true;
        writer.WishlistItems.Add(changed.AddItem(variant.Id, false));
        await writer.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => noOp.SaveChangesAsync());
        await using var fresh = store.Context();
        Assert.Equal(2, (await fresh.Wishlists.SingleAsync()).MembershipVersion);
        Assert.Equal(2, await fresh.WishlistItems.CountAsync());
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "agora-wishlist-" + Guid.NewGuid().ToString("N") + ".db");
        public AgoraDbContext Context() => new(new DbContextOptionsBuilder<AgoraDbContext>()
            .UseSqlite($"Data Source={_path};Pooling=False").Options);

        public static async Task<Store> Create()
        {
            var store = new Store();
            await using var db = store.Context();
            await db.Database.EnsureCreatedAsync();
            var customer = new Customer { Email = "owner@test.local", FullName = "Owner" };
            var category = new Category { Name = "Category", Slug = "category" };
            var product = new Product { CategoryId = category.Id, Name = "Product", Slug = "product" };
            foreach (var sku in new[] { "A", "B", "C" }) product.Variants.Add(new ProductVariant
            {
                ProductId = product.Id, Sku = sku, Name = sku, Price = new Money(10),
            });
            var wishlist = new Wishlist { CustomerId = customer.Id, Name = "List" };
            wishlist.AddItem(product.Variants[0].Id, false);
            db.AddRange(customer, category, product, wishlist);
            await db.SaveChangesAsync();
            return store;
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }
}
