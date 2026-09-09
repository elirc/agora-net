using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence;

public sealed class ShippingEligibilityPolicyConfiguration : IEntityTypeConfiguration<ShippingEligibilityPolicy>
{
    public void Configure(EntityTypeBuilder<ShippingEligibilityPolicy> policy)
    {
        policy.HasKey(p => p.ShippingMethodId); policy.Property(p => p.Revision).IsConcurrencyToken();
        policy.Property(p => p.AllowedCountriesJson).HasMaxLength(512).IsRequired();
        policy.HasOne<ShippingMethod>().WithOne().HasForeignKey<ShippingEligibilityPolicy>(p => p.ShippingMethodId).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class DeliveryCalendarConfiguration : IEntityTypeConfiguration<DeliveryCalendar>
{
    public void Configure(EntityTypeBuilder<DeliveryCalendar> calendar)
    {
        calendar.ToTable("DeliveryCalendars", table => table.HasCheckConstraint("CK_DeliveryCalendars_Singleton", "Id = 1"));
        calendar.Property(c => c.Id).ValueGeneratedNever(); calendar.Property(c => c.Revision).IsConcurrencyToken();
        calendar.HasMany(c => c.Closures).WithOne().HasForeignKey(c => c.DeliveryCalendarId).OnDelete(DeleteBehavior.Cascade);
        calendar.HasData(new { Id = 1, Enabled = false, CutoffUtcMinute = 840, Revision = 0L });
    }
}
public sealed class DeliveryCalendarClosureConfiguration : IEntityTypeConfiguration<DeliveryCalendarClosure>
{
    public void Configure(EntityTypeBuilder<DeliveryCalendarClosure> closure) => closure.HasKey(c => new { c.DeliveryCalendarId, c.Date });
}
