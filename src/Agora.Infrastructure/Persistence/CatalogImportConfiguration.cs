using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence;

public sealed class CatalogImportConfiguration : IEntityTypeConfiguration<CatalogImport>
{
    public void Configure(EntityTypeBuilder<CatalogImport> builder)
    {
        builder.ToTable("CatalogImports");
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Property(x => x.Digest).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProposalJson).IsRequired();
        builder.Property(x => x.ErrorsJson).IsRequired();
        builder.HasIndex(x => new { x.CreatedAt, x.Id });
        builder.HasMany(x => x.Results).WithOne().HasForeignKey(x => x.CatalogImportId).OnDelete(DeleteBehavior.Cascade);
        // Author and result product identifiers are historical attribution, not lifetime dependencies.
    }
}
public sealed class CatalogImportResultConfiguration : IEntityTypeConfiguration<CatalogImportResult>
{
    public void Configure(EntityTypeBuilder<CatalogImportResult> builder)
    {
        builder.ToTable("CatalogImportResults");
        builder.Property(x => x.RowKey).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => new { x.CatalogImportId, x.RowKey }).IsUnique();
        builder.HasIndex(x => new { x.CatalogImportId, x.Position }).IsUnique();
    }
}
