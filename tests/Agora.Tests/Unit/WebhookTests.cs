using Agora.Domain.Entities;
using Agora.Infrastructure.Services;

namespace Agora.Tests.Unit;

public class WebhookTests
{
    [Fact]
    public void SubscribesTo_IsCaseInsensitive()
    {
        var subscription = new WebhookSubscription { Events = ["order.paid"] };

        Assert.True(subscription.SubscribesTo("ORDER.PAID"));
        Assert.False(subscription.SubscribesTo("order.refunded"));
    }

    [Fact]
    public void KnownEvents_AreRecognized()
    {
        Assert.True(WebhookEvents.IsKnown("order.created"));
        Assert.True(WebhookEvents.IsKnown("order.fulfilled"));
        Assert.False(WebhookEvents.IsKnown("order.launched"));
    }

    [Fact]
    public void Signature_IsDeterministicHmacSha256()
    {
        var first = WebhookSigner.ComputeSignature("secret-key", "{\"a\":1}");
        var second = WebhookSigner.ComputeSignature("secret-key", "{\"a\":1}");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length); // 32 bytes hex
        Assert.NotEqual(first, WebhookSigner.ComputeSignature("other-key", "{\"a\":1}"));
        Assert.NotEqual(first, WebhookSigner.ComputeSignature("secret-key", "{\"a\":2}"));
    }

    [Fact]
    public void RecordAttempt_TracksOutcomeAndCount()
    {
        var delivery = new WebhookDelivery();
        var now = DateTimeOffset.UtcNow;

        delivery.RecordAttempt(false, 500, now);

        Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal(500, delivery.LastResponseStatusCode);
        Assert.True(delivery.CanRetry);

        delivery.RecordAttempt(true, 200, now);

        Assert.Equal(WebhookDeliveryStatus.Succeeded, delivery.Status);
        Assert.Equal(2, delivery.AttemptCount);
        Assert.False(delivery.CanRetry);
    }

    [Fact]
    public void CanRetry_StopsAtAttemptCap()
    {
        var delivery = new WebhookDelivery();
        for (var i = 0; i < WebhookDelivery.MaxAttempts; i++)
        {
            delivery.RecordAttempt(false, 500, DateTimeOffset.UtcNow);
        }

        Assert.False(delivery.CanRetry);
    }
}
