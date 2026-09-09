using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class ShipmentTrackingTests
{
    public static IEnumerable<object[]> Moves()
    {
        var allowed = new HashSet<(ShipmentTrackingStatus, ShipmentTrackingStatus)>
        {
            (ShipmentTrackingStatus.Unknown, ShipmentTrackingStatus.InTransit), (ShipmentTrackingStatus.Unknown, ShipmentTrackingStatus.Exception),
            (ShipmentTrackingStatus.InTransit, ShipmentTrackingStatus.OutForDelivery), (ShipmentTrackingStatus.InTransit, ShipmentTrackingStatus.Delivered),
            (ShipmentTrackingStatus.InTransit, ShipmentTrackingStatus.Exception), (ShipmentTrackingStatus.OutForDelivery, ShipmentTrackingStatus.Delivered),
            (ShipmentTrackingStatus.OutForDelivery, ShipmentTrackingStatus.Exception), (ShipmentTrackingStatus.Exception, ShipmentTrackingStatus.InTransit),
            (ShipmentTrackingStatus.Exception, ShipmentTrackingStatus.OutForDelivery), (ShipmentTrackingStatus.Exception, ShipmentTrackingStatus.Delivered)
        };
        foreach (var from in Enum.GetValues<ShipmentTrackingStatus>())
        foreach (var to in Enum.GetValues<ShipmentTrackingStatus>()) yield return [from, to, allowed.Contains((from, to))];
    }

    [Theory, MemberData(nameof(Moves))]
    public void Every_transition_has_an_explicit_contract_and_rejections_preserve_state(ShipmentTrackingStatus from, ShipmentTrackingStatus to, bool allowed)
    {
        var shipment = new Fulfillment(); var actor = Guid.NewGuid(); var now = DateTimeOffset.UnixEpoch;
        if (from is ShipmentTrackingStatus.InTransit or ShipmentTrackingStatus.OutForDelivery or ShipmentTrackingStatus.Delivered)
            shipment.RecordTracking(ShipmentTrackingStatus.InTransit, null, actor, now);
        if (from == ShipmentTrackingStatus.OutForDelivery) shipment.RecordTracking(from, null, actor, now);
        if (from == ShipmentTrackingStatus.Delivered) shipment.RecordTracking(from, null, actor, now);
        if (from == ShipmentTrackingStatus.Exception) shipment.RecordTracking(from, null, actor, now);
        var version = shipment.TrackingVersion;
        if (!allowed)
        {
            Assert.Throws<InvalidShipmentTrackingTransitionException>(() => shipment.RecordTracking(to, null, actor, now));
            Assert.Equal((from, version), (shipment.TrackingStatus, shipment.TrackingVersion)); return;
        }
        var entry = shipment.RecordTracking(to, "  Recorded  ", actor, now);
        Assert.Equal((to, version + 1), (shipment.TrackingStatus, shipment.TrackingVersion));
        Assert.Equal((shipment.Id, version + 1, to, "Recorded", actor, now),
            (entry.FulfillmentId, entry.Sequence, entry.Status, entry.Message, entry.ActorAdminId, entry.RecordedAt));
    }

    [Fact]
    public void Invalid_message_does_not_advance_the_parent()
    {
        var shipment = new Fulfillment();
        Assert.Throws<DomainException>(() => shipment.RecordTracking(ShipmentTrackingStatus.InTransit, new string('x', 201), Guid.NewGuid(), DateTimeOffset.UnixEpoch));
        Assert.Equal((ShipmentTrackingStatus.Unknown, 0L), (shipment.TrackingStatus, shipment.TrackingVersion));
    }
}
