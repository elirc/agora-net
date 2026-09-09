using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence;

public sealed class VariantQuantityPricingConfiguration : IEntityTypeConfiguration<VariantQuantityPricing>
{
    public void Configure(EntityTypeBuilder<VariantQuantityPricing> builder)
    {
        builder.ToTable("VariantQuantityPricing");
        builder.HasKey(p => p.ProductVariantId);
        builder.Property(p => p.Revision).IsConcurrencyToken();
        builder.HasOne<ProductVariant>().WithOne().HasForeignKey<VariantQuantityPricing>(p => p.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Tiers).WithOne().HasForeignKey(t => t.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class VariantQuantityTierConfiguration : IEntityTypeConfiguration<VariantQuantityTier>
{
    public void Configure(EntityTypeBuilder<VariantQuantityTier> builder)
    {
        builder.ToTable("VariantQuantityTiers");
        builder.HasKey(t => new { t.ProductVariantId, t.MinimumQuantity });
    }
}
