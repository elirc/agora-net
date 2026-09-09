using Agora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agora.Infrastructure.Persistence;

public sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> b)
    { b.ToTable("OutboxEvents"); b.Property(x => x.EventType).HasMaxLength(64).IsRequired(); b.Property(x => x.DataJson).HasMaxLength(65536).IsRequired(); }
}
public sealed class WebhookAttemptConfiguration : IEntityTypeConfiguration<WebhookAttempt>
{
    public void Configure(EntityTypeBuilder<WebhookAttempt> b)
    { b.ToTable("WebhookAttempts"); b.HasIndex(x => new { x.DeliveryId, x.AttemptNumber }).IsUnique(); b.Property(x => x.Outcome).IsConcurrencyToken();
        b.Property(x => x.ReasonCode).HasMaxLength(64); b.HasOne<WebhookDelivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Cascade); }
}
public sealed class WebhookDeliveryOutboxConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> b)
    {
        b.Property(x => x.DestinationUrl).HasMaxLength(2000);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.EventId, x.SubscriptionId }).IsUnique();
        b.HasOne(x => x.Event).WithMany(x => x.Deliveries).HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class WebhookReplayBatchConfiguration : IEntityTypeConfiguration<WebhookReplayBatch>
{
    public void Configure(EntityTypeBuilder<WebhookReplayBatch> b)
    { b.ToTable("WebhookReplayBatches"); b.Property(x => x.RequestDigest).HasMaxLength(64).IsRequired(); b.HasOne<WebhookSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict); b.HasMany(x => x.Results).WithOne().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade); }
}
public sealed class WebhookReplayResultConfiguration : IEntityTypeConfiguration<WebhookReplayResult>
{
    public void Configure(EntityTypeBuilder<WebhookReplayResult> b) { b.ToTable("WebhookReplayResults"); b.HasIndex(x => new { x.BatchId, x.EventId }).IsUnique(); }
}
