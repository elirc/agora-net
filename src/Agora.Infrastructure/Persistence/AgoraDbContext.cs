using System.Text.Json;
using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Agora.Infrastructure.Persistence;

public class AgoraDbContext(DbContextOptions<AgoraDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<DiscountCode> DiscountCodes => Set<DiscountCode>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewVote> ReviewVotes => Set<ReviewVote>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot order/compare DateTimeOffset or decimal natively;
        // store both as ordered-comparable longs (UTC ticks / cents).
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToUtcTicksConverter>();
        configurationBuilder.Properties<decimal>()
            .HaveConversion<DecimalToCentsConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(customer =>
        {
            customer.Property(c => c.Email).HasMaxLength(320).IsRequired();
            customer.HasIndex(c => c.Email).IsUnique();
            customer.Property(c => c.PasswordHash).HasMaxLength(500).IsRequired();
            customer.Property(c => c.FullName).HasMaxLength(200);
        });

        modelBuilder.Entity<Category>(category =>
        {
            category.Property(c => c.Name).HasMaxLength(200).IsRequired();
            category.Property(c => c.Slug).HasMaxLength(200).IsRequired();
            category.HasIndex(c => c.Slug).IsUnique();
            category.HasOne(c => c.ParentCategory)
                .WithMany()
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(product =>
        {
            product.Property(p => p.Name).HasMaxLength(200).IsRequired();
            product.Property(p => p.Slug).HasMaxLength(200).IsRequired();
            product.Property(p => p.Description).HasMaxLength(4000);
            product.HasIndex(p => p.Slug).IsUnique();
            product.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            product.HasMany(p => p.Variants)
                .WithOne(v => v.Product)
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            product.HasMany(p => p.Images)
                .WithOne(i => i.Product)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductVariant>(variant =>
        {
            variant.Property(v => v.Sku).HasMaxLength(64).IsRequired();
            variant.Property(v => v.Name).HasMaxLength(200);
            variant.HasIndex(v => v.Sku).IsUnique();
            variant.OwnsOne(v => v.Price, price =>
            {
                price.Property(m => m.Amount).HasColumnName("PriceAmount");
                price.Property(m => m.Currency).HasColumnName("PriceCurrency").HasMaxLength(3);
            });
            variant.Property(v => v.Options)
                .HasConversion(OptionsConverter, OptionsComparer);
        });

        modelBuilder.Entity<ProductImage>(image =>
        {
            image.Property(i => i.Url).HasMaxLength(2000).IsRequired();
            image.Property(i => i.AltText).HasMaxLength(500);
        });

        modelBuilder.Entity<InventoryItem>(inventory =>
        {
            inventory.HasIndex(i => i.ProductVariantId).IsUnique();
            inventory.HasOne(i => i.ProductVariant)
                .WithOne(v => v.Inventory)
                .HasForeignKey<InventoryItem>(i => i.ProductVariantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Cart>(cart =>
        {
            // Computed convenience view over Items; must not become a second navigation.
            cart.Ignore(c => c.ActiveItems);
            cart.Property(c => c.Token).HasMaxLength(64).IsRequired();
            cart.HasIndex(c => c.Token).IsUnique();
            cart.HasIndex(c => c.CustomerId);
            cart.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(c => c.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
            cart.HasMany(c => c.Items)
                .WithOne(i => i.Cart)
                .HasForeignKey(i => i.CartId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CartItem>(item =>
        {
            item.HasIndex(i => new { i.CartId, i.ProductVariantId }).IsUnique();
            item.HasOne(i => i.ProductVariant)
                .WithMany()
                .HasForeignKey(i => i.ProductVariantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DiscountCode>(discount =>
        {
            discount.Property(d => d.Code).HasMaxLength(64).IsRequired();
            discount.HasIndex(d => d.Code).IsUnique();
            discount.Property(d => d.Currency).HasMaxLength(3);
        });

        modelBuilder.Entity<Order>(order =>
        {
            order.Property(o => o.Number).HasMaxLength(32).IsRequired();
            order.Property(o => o.Email).HasMaxLength(320).IsRequired();
            order.Property(o => o.Currency).HasMaxLength(3);
            order.Property(o => o.DiscountCode).HasMaxLength(64);
            order.Property(o => o.ShippingMethodCode).HasMaxLength(64);
            order.Property(o => o.ShippingMethodName).HasMaxLength(200);
            order.HasIndex(o => o.Number).IsUnique();
            order.HasIndex(o => o.CustomerId);
            order.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
            order.OwnsOne(o => o.ShippingAddress);
            order.HasMany(o => o.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerAddress>(address =>
        {
            address.Property(a => a.Label).HasMaxLength(100);
            address.HasIndex(a => a.CustomerId);
            address.OwnsOne(a => a.Address);
            address.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(a => a.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShippingMethod>(method =>
        {
            method.Property(m => m.Code).HasMaxLength(64).IsRequired();
            method.HasIndex(m => m.Code).IsUnique();
            method.Property(m => m.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<Wishlist>(wishlist =>
        {
            wishlist.Property(w => w.Name).HasMaxLength(100).IsRequired();
            wishlist.HasIndex(w => new { w.CustomerId, w.Name }).IsUnique();
            wishlist.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(w => w.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
            wishlist.HasMany(w => w.Items)
                .WithOne(i => i.Wishlist)
                .HasForeignKey(i => i.WishlistId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WishlistItem>(item =>
        {
            item.HasIndex(i => new { i.WishlistId, i.ProductVariantId }).IsUnique();
            item.HasOne(i => i.ProductVariant)
                .WithMany()
                .HasForeignKey(i => i.ProductVariantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Review>(review =>
        {
            review.Property(r => r.Title).HasMaxLength(200);
            review.Property(r => r.Body).HasMaxLength(4000).IsRequired();
            review.Property(r => r.ModerationNote).HasMaxLength(500);
            review.HasIndex(r => new { r.ProductId, r.CustomerId }).IsUnique();
            review.HasIndex(r => r.Status);
            review.HasOne(r => r.Product)
                .WithMany()
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            review.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(r => r.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReviewVote>(vote =>
        {
            vote.HasIndex(v => new { v.ReviewId, v.CustomerId }).IsUnique();
            vote.HasOne(v => v.Review)
                .WithMany()
                .HasForeignKey(v => v.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);
            vote.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(v => v.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(item =>
        {
            item.Property(i => i.Sku).HasMaxLength(64).IsRequired();
            item.Property(i => i.ProductName).HasMaxLength(200);
            item.Property(i => i.VariantName).HasMaxLength(200);
        });
    }

    private static readonly ValueConverter<Dictionary<string, string>, string> OptionsConverter =
        new(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null)
                 ?? new Dictionary<string, string>());

    private static readonly ValueComparer<Dictionary<string, string>> OptionsComparer =
        new(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                      == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(
                     JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                     (JsonSerializerOptions?)null)
                 ?? new Dictionary<string, string>());
}
