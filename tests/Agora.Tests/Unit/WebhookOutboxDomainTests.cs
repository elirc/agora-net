using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class WebhookOutboxDomainTests
{
    [Fact]
    public void Expired_uncertain_slot_is_consumed_and_late_ack_cannot_overwrite_new_generation()
    {
        var start = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var delivery = Delivery(start); var first = delivery.Claim(start);
        Assert.Equal(1, delivery.AttemptCount); Assert.Equal(1, first.AttemptNumber);
        Assert.True(delivery.ExpireLease(start.AddSeconds(61)));
        first.Finish(WebhookAttemptOutcome.Unknown, start.AddSeconds(61), reasonCode: "LeaseExpired");
        var second = delivery.Claim(start.AddSeconds(61));
        Assert.Equal(2, delivery.AttemptCount); Assert.True(second.LeaseGeneration > first.LeaseGeneration);
        Assert.False(delivery.Complete(first.LeaseGeneration, true, 200, start.AddSeconds(62)));
        Assert.True(delivery.Complete(second.LeaseGeneration, true, 200, start.AddSeconds(62)));
        second.Finish(WebhookAttemptOutcome.Succeeded, start.AddSeconds(62), 200);
        Assert.Equal(WebhookDeliveryStatus.Succeeded, delivery.Status);
        Assert.Equal(WebhookAttemptOutcome.Unknown, first.Outcome); Assert.Equal(WebhookAttemptOutcome.Succeeded, second.Outcome);
    }

    [Fact]
    public void Claim_reserves_exactly_five_slots_and_cancel_only_stops_unsent_work()
    {
        var now = DateTimeOffset.UtcNow; var delivery = Delivery(now);
        for (var i = 1; i <= WebhookDelivery.MaxAttempts; i++)
        {
            var attempt = delivery.Claim(now); Assert.Equal(i, attempt.AttemptNumber);
            Assert.True(delivery.Complete(attempt.LeaseGeneration, false, 503, now));
            now = delivery.DueAt ?? now;
        }
        Assert.Throws<Agora.Domain.Common.InvalidWebhookDeliveryException>(() => delivery.Claim(now));
        var queued = Delivery(now); queued.Cancel(); Assert.Equal(WebhookDeliveryStatus.Cancelled, queued.Status);
        delivery.Cancel(); Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status);
    }

    private static WebhookDelivery Delivery(DateTimeOffset now)
    {
        var delivery = new WebhookDelivery { SubscriptionId = Guid.NewGuid(), EventType = WebhookEvents.OrderPaid,
            Payload = "{}", Signature = "signature", DestinationUrl = "https://example.test", CreatedAt = now };
        delivery.Queue(now); return delivery;
    }
}
