using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence.Configurations;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b) { b.Property(x=>x.Name).HasMaxLength(120).IsRequired(); b.Property(x=>x.Reference).HasMaxLength(120); b.Property(x=>x.Version).IsConcurrencyToken(); b.HasIndex(x=>new{x.Name,x.Id}); }
}
public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b) { b.Property(x=>x.Revision).IsConcurrencyToken(); b.HasOne(x=>x.Supplier).WithMany().HasForeignKey(x=>x.SupplierId).OnDelete(DeleteBehavior.Restrict); b.HasMany(x=>x.Lines).WithOne().HasForeignKey(x=>x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade); b.HasMany(x=>x.Receipts).WithOne().HasForeignKey(x=>x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade); }
}
public sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b) { b.Property(x=>x.Sku).HasMaxLength(64).IsRequired(); b.Property(x=>x.VariantName).HasMaxLength(120).IsRequired(); b.HasIndex(x=>new{x.PurchaseOrderId,x.ProductVariantId}).IsUnique(); b.HasOne<ProductVariant>().WithMany().HasForeignKey(x=>x.ProductVariantId).OnDelete(DeleteBehavior.SetNull); }
}
public sealed class PurchaseOrderReceiptConfiguration : IEntityTypeConfiguration<PurchaseOrderReceipt>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderReceipt> b) { b.Property(x=>x.Id).ValueGeneratedNever(); b.Property(x=>x.Fingerprint).HasMaxLength(64).IsRequired(); b.HasMany(x=>x.Lines).WithOne().HasForeignKey(x=>x.ReceiptId).OnDelete(DeleteBehavior.Cascade); }
}
public sealed class PurchaseOrderReceiptLineConfiguration : IEntityTypeConfiguration<PurchaseOrderReceiptLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderReceiptLine> b) { b.Property(x=>x.Sku).HasMaxLength(64).IsRequired(); b.HasIndex(x=>new{x.ReceiptId,x.PurchaseOrderLineId}).IsUnique(); b.HasOne<ProductVariant>().WithMany().HasForeignKey(x=>x.ProductVariantId).OnDelete(DeleteBehavior.SetNull); }
}
public sealed class InventoryCountSessionConfiguration : IEntityTypeConfiguration<InventoryCountSession>
{
    public void Configure(EntityTypeBuilder<InventoryCountSession> b) { b.Property(x=>x.Revision).IsConcurrencyToken(); b.HasMany(x=>x.Lines).WithOne().HasForeignKey(x=>x.SessionId).OnDelete(DeleteBehavior.Cascade); }
}
public sealed class InventoryCountLineConfiguration : IEntityTypeConfiguration<InventoryCountLine>
{
    public void Configure(EntityTypeBuilder<InventoryCountLine> b) { b.Property(x=>x.Sku).HasMaxLength(64).IsRequired(); b.HasIndex(x=>new{x.SessionId,x.ProductVariantId}).IsUnique(); b.HasOne<ProductVariant>().WithMany().HasForeignKey(x=>x.ProductVariantId).OnDelete(DeleteBehavior.SetNull); }
}
