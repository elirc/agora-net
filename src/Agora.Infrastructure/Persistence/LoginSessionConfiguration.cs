using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence;

public sealed class LoginSessionConfiguration : IEntityTypeConfiguration<LoginSession>
{
    public void Configure(EntityTypeBuilder<LoginSession> session)
    {
        session.Property(s => s.IssuedRole).HasMaxLength(32).IsRequired();
        session.Property(s => s.DeviceLabel).HasMaxLength(80);
        session.HasIndex(s => new { s.CustomerId, s.ExpiresAt });
        session.HasIndex(s => s.ExpiresAt);
        session.HasOne<Customer>().WithMany().HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
