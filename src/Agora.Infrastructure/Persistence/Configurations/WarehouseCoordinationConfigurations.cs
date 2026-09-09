using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Agora.Infrastructure.Persistence.Configurations;
public sealed class OrderHoldConfiguration:IEntityTypeConfiguration<OrderHold>{public void Configure(EntityTypeBuilder<OrderHold>b){b.Property(x=>x.Note).HasMaxLength(500);b.Property(x=>x.Revision).IsConcurrencyToken();b.HasOne<Order>().WithMany().HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Cascade);b.HasIndex(x=>x.OrderId).HasFilter("IsActive = 1").IsUnique();b.HasIndex(x=>new{x.OrderId,x.CreatedAt,x.Id});}}
public sealed class WarehouseAssignmentConfiguration:IEntityTypeConfiguration<WarehouseAssignment>{public void Configure(EntityTypeBuilder<WarehouseAssignment>b){b.HasKey(x=>x.OrderId);b.Property(x=>x.Revision).IsConcurrencyToken();b.HasIndex(x=>x.AssignmentId).IsUnique();b.HasOne<Order>().WithOne().HasForeignKey<WarehouseAssignment>(x=>x.OrderId).OnDelete(DeleteBehavior.Cascade);}}
