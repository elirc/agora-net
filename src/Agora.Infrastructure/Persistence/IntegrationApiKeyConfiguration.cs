using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence;

public sealed class IntegrationApiKeyConfiguration : IEntityTypeConfiguration<IntegrationApiKey>
{
    public void Configure(EntityTypeBuilder<IntegrationApiKey> builder)
    {
        builder.ToTable("IntegrationApiKeys", table => table.HasCheckConstraint("CK_IntegrationApiKeys_DigestLength", "length(SecretDigest) = 32"));
        builder.Property(k => k.Id).ValueGeneratedNever();
        builder.Property(k => k.Name).HasMaxLength(80).IsRequired();
        builder.Property(k => k.SecretDigest).HasMaxLength(32).IsRequired();
        builder.HasIndex(k => new { k.CreatedAt, k.Id });
        builder.HasIndex(k => k.ExpiresAt);
        // CreatorId is retained attribution even if the administrator is later removed.
    }
}
