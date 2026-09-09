using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence;

public sealed class ReportExportJobConfiguration : IEntityTypeConfiguration<ReportExportJob>
{
    public void Configure(EntityTypeBuilder<ReportExportJob> job)
    {
        job.Property(x => x.LeaseGeneration).IsConcurrencyToken();
        job.Property(x => x.FailureCode).HasMaxLength(64);
        job.HasIndex(x => new { x.RequesterId, x.Status });
        job.HasOne<Customer>().WithMany().HasForeignKey(x => x.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ReportExportArtifactConfiguration : IEntityTypeConfiguration<ReportExportArtifact>
{
    public void Configure(EntityTypeBuilder<ReportExportArtifact> artifact)
    {
        artifact.HasKey(x => x.JobId);
        artifact.Property(x => x.Content).HasMaxLength(10 * 1024 * 1024).IsRequired();
        artifact.Property(x => x.Digest).HasMaxLength(64).IsRequired();
        artifact.HasOne<ReportExportJob>().WithOne()
            .HasForeignKey<ReportExportArtifact>(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}
