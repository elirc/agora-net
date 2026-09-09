using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence;

public sealed class GuestOrderCredentialConfiguration : IEntityTypeConfiguration<GuestOrderCredential>
{
    public void Configure(EntityTypeBuilder<GuestOrderCredential> credential)
    {
        credential.Property(c => c.SecretDigest).HasMaxLength(32).IsRequired();
        credential.HasIndex(c => c.OrderId).IsUnique().HasFilter("RevokedAt IS NULL");
        credential.HasIndex(c => c.ExpiresAt);
        credential.HasOne<Order>().WithMany().HasForeignKey(c => c.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
