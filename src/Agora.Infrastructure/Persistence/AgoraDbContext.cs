using System.Text.Json;
using Agora.Domain.Entities;
using Agora.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Agora.Infrastructure.Persistence;

public class AgoraDbContext(DbContextOptions<AgoraDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<OrderHold> OrderHolds => Set<OrderHold>();
    public DbSet<WarehouseAssignment> WarehouseAssignments => Set<WarehouseAssignment>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseOrderReceipt> PurchaseOrderReceipts => Set<PurchaseOrderReceipt>();
    public DbSet<PurchaseOrderReceiptLine> PurchaseOrderReceiptLines => Set<PurchaseOrderReceiptLine>();
    public DbSet<InventoryCountSession> InventoryCountSessions => Set<InventoryCountSession>();
    public DbSet<InventoryCountLine> InventoryCountLines => Set<InventoryCountLine>();
    public DbSet<CategoryTreeState> CategoryTreeStates => Set<CategoryTreeState>();
    public DbSet<CategoryOptionSchema> CategoryOptionSchemas => Set<CategoryOptionSchema>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<CatalogChange> CatalogChanges => Set<CatalogChange>();
    public DbSet<CatalogFeedState> CatalogFeedStates => Set<CatalogFeedState>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ProductTag> ProductTags => Set<ProductTag>();
    public DbSet<ProductCollection> ProductCollections => Set<ProductCollection>();
    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryReorderPolicy> InventoryReorderPolicies => Set<InventoryReorderPolicy>();
    public DbSet<InventoryAdjustmentBatch> InventoryAdjustmentBatches => Set<InventoryAdjustmentBatch>();
    public DbSet<InventoryAdjustmentLine> InventoryAdjustmentLines => Set<InventoryAdjustmentLine>();
    public DbSet<SavedCatalogSearch> SavedCatalogSearches => Set<SavedCatalogSearch>();
    public DbSet<RecentlyViewedProduct> RecentlyViewedProducts => Set<RecentlyViewedProduct>();
    public DbSet<ReviewReport> ReviewReports => Set<ReviewReport>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<CartTemplate> CartTemplates => Set<CartTemplate>();
    public DbSet<CartTemplateLine> CartTemplateLines => Set<CartTemplateLine>();
    public DbSet<CheckoutPreference> CheckoutPreferences => Set<CheckoutPreference>();
    public DbSet<DiscountCode> DiscountCodes => Set<DiscountCode>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewVote> ReviewVotes => Set<ReviewVote>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();
    public DbSet<ReturnRequestItem> ReturnRequestItems => Set<ReturnRequestItem>();
    public DbSet<ReturnEvidence> ReturnEvidence => Set<ReturnEvidence>();
    public DbSet<OrderSupportNote> OrderSupportNotes => Set<OrderSupportNote>();
    public DbSet<ShipmentTrackingEvent> ShipmentTrackingEvents => Set<ShipmentTrackingEvent>();
    public DbSet<Fulfillment> Fulfillments => Set<Fulfillment>();
    public DbSet<FulfillmentItem> FulfillmentItems => Set<FulfillmentItem>();
    public DbSet<TaxCategory> TaxCategories => Set<TaxCategory>();
    public DbSet<TaxZone> TaxZones => Set<TaxZone>();
    public DbSet<TaxZoneRate> TaxZoneRates => Set<TaxZoneRate>();
    public DbSet<GiftCard> GiftCards => Set<GiftCard>();
    public DbSet<GiftCardEntry> GiftCardEntries => Set<GiftCardEntry>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

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
        modelBuilder.ApplyConfiguration(new CatalogChangeConfiguration());
        modelBuilder.ApplyConfiguration(new CatalogFeedStateConfiguration());
        modelBuilder.Entity<Product>().Property(p => p.CatalogRevision).IsConcurrencyToken();
        modelBuilder.Entity<Order>().HasIndex(o => new { o.CustomerId, o.CreatedAt, o.Number })
            .IsDescending(false, true, true).HasDatabaseName("IX_Orders_CustomerId_CreatedAt_Number");
        modelBuilder.ApplyConfiguration(new LoginSessionConfiguration());
        modelBuilder.ApplyConfiguration(new IntegrationApiKeyConfiguration());
        modelBuilder.ApplyConfiguration(new ReportExportJobConfiguration());
        modelBuilder.ApplyConfiguration(new ReportExportArtifactConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxEventConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookDeliveryOutboxConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookReplayBatchConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookReplayResultConfiguration());
        modelBuilder.ApplyConfiguration(new OrderHoldConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new GuestOrderCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new VariantQuantityPricingConfiguration());
        modelBuilder.ApplyConfiguration(new VariantQuantityTierConfiguration());
        modelBuilder.ApplyConfiguration(new ShippingEligibilityPolicyConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryCalendarConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryCalendarClosureConfiguration());
        modelBuilder.ApplyConfiguration(new SupplierConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderLineConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderReceiptLineConfiguration());
        modelBuilder.ApplyConfiguration(new InventoryCountSessionConfiguration());
        modelBuilder.ApplyConfiguration(new InventoryCountLineConfiguration());
        modelBuilder.ApplyConfiguration(new CatalogImportConfiguration());
        modelBuilder.ApplyConfiguration(new CatalogImportResultConfiguration());
        modelBuilder.Entity<CategoryOptionSchema>(schema =>
        {
            schema.HasKey(s => s.CategoryId);
            schema.Property(s => s.Revision).IsConcurrencyToken();
            schema.Property(s => s.RulesJson).HasMaxLength(262144).IsRequired();
            schema.HasOne<Category>().WithOne().HasForeignKey<CategoryOptionSchema>(s => s.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CategoryTreeState>(state =>
        {
            state.ToTable("CategoryTreeStates", table => table.HasCheckConstraint("CK_CategoryTreeStates_Singleton", "Id = 1"));
            state.Property(s => s.Id).ValueGeneratedNever();
            state.Property(s => s.Version).IsConcurrencyToken();
            state.HasData(new { Id = 1, Version = 0L });
        });
        modelBuilder.Entity<GiftCardEntry>(entry =>
        {
            entry.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entry.HasIndex(e => new { e.GiftCardId, e.RecordedVersion }).IsUnique();
            entry.HasOne<GiftCard>().WithMany().HasForeignKey(e => e.GiftCardId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ReturnEvidence>(evidence =>
        {
            evidence.Property(e => e.Url).HasMaxLength(2000).IsRequired();
            evidence.Property(e => e.Description).HasMaxLength(200);
            evidence.HasIndex(e => new { e.ReturnRequestId, e.CreatedAt, e.Id });
            evidence.HasOne<ReturnRequest>().WithMany().HasForeignKey(e => e.ReturnRequestId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<OrderSupportNote>(note =>
        {
            note.Property(n => n.Body).HasMaxLength(1000).IsRequired();
            note.HasIndex(n => new { n.OrderId, n.CreatedAt, n.Id }).IsDescending(false, true, false);
            note.HasOne<Order>().WithMany().HasForeignKey(n => n.OrderId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Fulfillment>().Property(f => f.TrackingVersion).IsConcurrencyToken();
        modelBuilder.Entity<ShipmentTrackingEvent>(tracking =>
        {
            tracking.Property(e => e.Message).HasMaxLength(200);
            tracking.HasIndex(e => new { e.FulfillmentId, e.Sequence }).IsUnique();
            tracking.HasOne<Fulfillment>().WithMany().HasForeignKey(e => e.FulfillmentId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CheckoutPreference>(preference =>
        {
            preference.HasKey(p => p.CustomerId);
            preference.Property(p => p.Version).IsConcurrencyToken();
            preference.Property(p => p.ShippingMethodCode).HasMaxLength(64);
            preference.HasOne<Customer>().WithOne().HasForeignKey<CheckoutPreference>(p => p.CustomerId).OnDelete(DeleteBehavior.Cascade);
            preference.HasOne<CustomerAddress>().WithMany().HasForeignKey(p => p.ShippingAddressId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<CartTemplate>(template =>
        {
            template.Property(t => t.Name).HasMaxLength(80).IsRequired();
            template.HasIndex(t => new { t.CustomerId, t.CreatedAt, t.Id });
            template.HasOne<Customer>().WithMany().HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.Cascade);
            template.HasMany(t => t.Lines).WithOne().HasForeignKey(l => l.CartTemplateId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CartTemplateLine>(line =>
        {
            line.HasIndex(l => new { l.CartTemplateId, l.VariantId }).IsUnique();
            line.Property(l => l.Sku).HasMaxLength(64).IsRequired();
            line.Property(l => l.ProductName).HasMaxLength(200).IsRequired();
            line.Property(l => l.VariantName).HasMaxLength(200).IsRequired();
        });
        modelBuilder.Entity<ReviewReport>(report =>
        {
            report.Property(r => r.Comment).HasMaxLength(500);
            report.Property(r => r.ResolutionNote).HasMaxLength(500);
            report.Property(r => r.Version).IsConcurrencyToken();
            report.HasIndex(r => new { r.CustomerId, r.ReviewId }).IsUnique();
            report.HasIndex(r => new { r.Status, r.CreatedAt, r.Id });
            report.HasOne(r => r.Review).WithMany().HasForeignKey(r => r.ReviewId).OnDelete(DeleteBehavior.Cascade);
            report.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SavedCatalogSearch>(saved =>
        {
            saved.Property(s => s.Name).HasMaxLength(80).IsRequired();
            saved.Property(s => s.DefinitionJson).HasMaxLength(SavedCatalogSearch.MaximumDefinitionLength).IsRequired();
            saved.HasIndex(s => new { s.CustomerId, s.CreatedAt, s.Id });
            saved.HasOne<Customer>().WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<RecentlyViewedProduct>(recent =>
        {
            recent.HasKey(r => new { r.CustomerId, r.ProductId });
            recent.HasIndex(r => new { r.CustomerId, r.LastViewedAt, r.ProductId }).IsDescending(false, true, false);
            recent.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Cascade);
            recent.HasOne(r => r.Product).WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<InventoryAdjustmentBatch>(batch =>
        {
            batch.Property(b => b.Id).ValueGeneratedNever();
            batch.Property(b => b.Reason).HasMaxLength(200).IsRequired();
            batch.Property(b => b.Fingerprint).HasMaxLength(64).IsRequired();
            batch.HasMany(b => b.Lines).WithOne().HasForeignKey(l => l.BatchId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<InventoryAdjustmentLine>(line =>
        {
            line.Property(l => l.Sku).HasMaxLength(64).IsRequired();
            line.HasIndex(l => new { l.BatchId, l.VariantId }).IsUnique();
            // No variant/actor foreign key: completed receipts survive catalog/account removal.
        });
        modelBuilder.Entity<InventoryReorderPolicy>(policy =>
        {
            policy.HasKey(p => p.ProductVariantId);
            policy.Property(p => p.Version).IsConcurrencyToken();
            policy.HasOne<ProductVariant>().WithOne().HasForeignKey<InventoryReorderPolicy>(p => p.ProductVariantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
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
            product.Property(p => p.TagVersion).IsConcurrencyToken();
            product.Property(p => p.ImageRevision).IsConcurrencyToken();
            product.Property(p => p.Name).HasMaxLength(200).IsRequired();
            product.Property(p => p.Slug).HasMaxLength(200).IsRequired();
            product.Property(p => p.Description).HasMaxLength(4000);
            product.HasIndex(p => p.Slug).IsUnique();
            product.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            product.HasOne(p => p.TaxCategory)
                .WithMany()
                .HasForeignKey(p => p.TaxCategoryId)
                .OnDelete(DeleteBehavior.SetNull);
            product.HasMany(p => p.Variants)
                .WithOne(v => v.Product)
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            product.HasMany(p => p.Images)
                .WithOne(i => i.Product)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Tag>(tag =>
        {
            tag.Property(t => t.Name).HasMaxLength(60).IsRequired();
            tag.Property(t => t.Slug).HasMaxLength(60).IsRequired();
            tag.HasIndex(t => t.Slug).IsUnique();
        });
        modelBuilder.Entity<ProductTag>(tag =>
        {
            tag.HasKey(t => new { t.ProductId, t.TagId });
            tag.HasOne(t => t.Product).WithMany(p => p.Tags).HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Cascade);
            tag.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ProductCollection>(collection =>
        {
            collection.Property(c => c.Title).HasMaxLength(120).IsRequired();
            collection.Property(c => c.Slug).HasMaxLength(60).IsRequired();
            collection.Property(c => c.Version).IsConcurrencyToken();
            collection.HasIndex(c => c.Slug).IsUnique();
            collection.HasMany(c => c.Items).WithOne(i => i.Collection).HasForeignKey(i => i.CollectionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CollectionItem>(item =>
        {
            item.HasKey(i => new { i.CollectionId, i.ProductId });
            item.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
            item.HasIndex(i => new { i.CollectionId, i.Position });
        });

        modelBuilder.Entity<ProductVariant>(variant =>
        {
            variant.Property(v => v.Version).IsConcurrencyToken();
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
            inventory.Property(i => i.Version).IsConcurrencyToken();
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
            cart.Property(c => c.Version).IsConcurrencyToken();
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

        modelBuilder.Entity<WebhookSubscription>(subscription =>
        {
            subscription.Property(s => s.Url).HasMaxLength(2000).IsRequired();
            subscription.Property(s => s.Secret).HasMaxLength(200).IsRequired();
            subscription.Property(s => s.Events)
                .HasConversion(EventsConverter, EventsComparer)
                .HasMaxLength(1000);
        });

        modelBuilder.Entity<WebhookDelivery>(delivery =>
        {
            delivery.Property(d => d.EventType).HasMaxLength(64).IsRequired();
            delivery.Property(d => d.Signature).HasMaxLength(128);
            delivery.HasIndex(d => d.SubscriptionId);
            delivery.HasIndex(d => d.Status);
            delivery.HasOne(d => d.Subscription)
                .WithMany()
                .HasForeignKey(d => d.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Fulfillment>(fulfillment =>
        {
            fulfillment.Property(f => f.Number).HasMaxLength(32).IsRequired();
            fulfillment.HasIndex(f => f.Number).IsUnique();
            fulfillment.Property(f => f.Carrier).HasMaxLength(100);
            fulfillment.Property(f => f.TrackingNumber).HasMaxLength(100);
            fulfillment.HasOne(f => f.Order)
                .WithMany()
                .HasForeignKey(f => f.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            fulfillment.HasMany(f => f.Items)
                .WithOne(i => i.Fulfillment)
                .HasForeignKey(i => i.FulfillmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FulfillmentItem>(item =>
        {
            item.Property(i => i.Sku).HasMaxLength(64).IsRequired();
            item.HasIndex(i => i.OrderItemId);
        });

        modelBuilder.Entity<TaxCategory>(category =>
        {
            category.Property(c => c.Code).HasMaxLength(64).IsRequired();
            category.HasIndex(c => c.Code).IsUnique();
            category.Property(c => c.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<TaxZone>(zone =>
        {
            zone.Property(z => z.Code).HasMaxLength(64).IsRequired();
            zone.HasIndex(z => z.Code).IsUnique();
            zone.Property(z => z.Name).HasMaxLength(200).IsRequired();
            zone.Property(z => z.Country).HasMaxLength(2).IsRequired();
            zone.Property(z => z.Region).HasMaxLength(100);
            // Rates need sub-cent precision (e.g. 0.095); override the global
            // decimal->cents convention.
            zone.Property(z => z.DefaultRate)
                .HasConversion<DecimalRateToMillionthsConverter>();
            zone.HasIndex(z => new { z.Country, z.Region });
            zone.HasMany(z => z.Rates)
                .WithOne(r => r.TaxZone)
                .HasForeignKey(r => r.TaxZoneId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaxZoneRate>(rate =>
        {
            rate.HasIndex(r => new { r.TaxZoneId, r.TaxCategoryId }).IsUnique();
            rate.Property(r => r.Rate)
                .HasConversion<DecimalRateToMillionthsConverter>();
            rate.HasOne(r => r.TaxCategory)
                .WithMany()
                .HasForeignKey(r => r.TaxCategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GiftCard>(card =>
        {
            card.Property(c => c.Code).HasMaxLength(32).IsRequired();
            card.HasIndex(c => c.Code).IsUnique();
            card.Property(c => c.Currency).HasMaxLength(3);
            card.Property(c => c.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<ReturnRequest>(request =>
        {
            request.Property(r => r.Number).HasMaxLength(32).IsRequired();
            request.HasIndex(r => r.Number).IsUnique();
            request.Property(r => r.Comment).HasMaxLength(500);
            request.Property(r => r.RejectionNote).HasMaxLength(500);
            request.Property(r => r.Currency).HasMaxLength(3);
            request.HasIndex(r => r.Status);
            request.HasOne(r => r.Order)
                .WithMany()
                .HasForeignKey(r => r.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            request.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(r => r.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
            request.HasMany(r => r.Items)
                .WithOne(i => i.ReturnRequest)
                .HasForeignKey(i => i.ReturnRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReturnRequestItem>(item =>
        {
            item.Property(i => i.Sku).HasMaxLength(64).IsRequired();
            item.HasIndex(i => i.OrderItemId);
        });

        modelBuilder.Entity<Wishlist>(wishlist =>
        {
            wishlist.Property(w => w.MembershipVersion).IsConcurrencyToken();
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
            item.Property(i => i.Note).HasMaxLength(500);
            item.Property(i => i.NoteVersion).IsConcurrencyToken();
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

    private static readonly ValueConverter<List<string>, string> EventsConverter =
        new(
            v => string.Join(',', v),
            v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList());

    private static readonly ValueComparer<List<string>> EventsComparer =
        new(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
            v => string.Join(',', v).GetHashCode(),
            v => v.ToList());

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
