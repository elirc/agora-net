using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Agora.Infrastructure.Persistence.Configurations;
public sealed class CatalogChangeConfiguration:IEntityTypeConfiguration<CatalogChange>
{public void Configure(EntityTypeBuilder<CatalogChange>b){b.HasKey(x=>x.Sequence);b.Property(x=>x.Sequence).ValueGeneratedOnAdd().HasAnnotation("Sqlite:Autoincrement",true);b.Property(x=>x.PayloadJson).HasMaxLength(262_144);b.HasIndex(x=>x.CreatedAt);b.HasIndex(x=>new{x.ProductId,x.ProductRevision});}}
public sealed class CatalogFeedStateConfiguration:IEntityTypeConfiguration<CatalogFeedState>
{public void Configure(EntityTypeBuilder<CatalogFeedState>b){b.ToTable("CatalogFeedStates",t=>t.HasCheckConstraint("CK_CatalogFeedStates_Singleton","Id = 1"));b.Property(x=>x.Id).ValueGeneratedNever();b.Property(x=>x.Version).IsConcurrencyToken();b.HasData(new{Id=1,LastCommittedSequence=0L,LastPurgedSequence=0L,Version=0L});}}
