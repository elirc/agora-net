using Agora.Domain.Entities;
namespace Agora.Tests.Unit;
public sealed class WarehouseCoordinationTests
{
 [Fact] public void Hold_release_is_revision_protected_and_terminal(){var actor=Guid.NewGuid();var h=new OrderHold(Guid.NewGuid(),OrderHoldReason.AddressQuestion," check ",actor,DateTimeOffset.UnixEpoch);Assert.Equal("check",h.Note);h.Release(actor,0,DateTimeOffset.UnixEpoch.AddHours(1));Assert.False(h.IsActive);Assert.Equal(1,h.Revision);Assert.Throws<WarehouseCoordinationConflictException>(()=>h.Release(actor,1,DateTimeOffset.UnixEpoch));}
 [Fact] public void Assignment_expiry_is_inclusive_and_replacement_changes_identity(){var now=DateTimeOffset.Parse("2026-01-01T10:00:00Z");var a=new WarehouseAssignment(Guid.NewGuid(),Guid.NewGuid(),now);var old=a.AssignmentId;Assert.True(a.IsLive(now.AddMinutes(15).AddTicks(-1)));Assert.False(a.IsLive(now.AddMinutes(15)));a.Replace(Guid.NewGuid(),now.AddMinutes(15));Assert.NotEqual(old,a.AssignmentId);Assert.Equal(2,a.Revision);}
 [Fact] public void Renewal_requires_owner_identity_revision_and_live_lease(){var now=DateTimeOffset.UnixEpoch;var owner=Guid.NewGuid();var a=new WarehouseAssignment(Guid.NewGuid(),owner,now);Assert.Throws<WarehouseCoordinationConflictException>(()=>a.Renew(Guid.NewGuid(),a.AssignmentId,1,now));Assert.Throws<WarehouseCoordinationConflictException>(()=>a.Renew(owner,a.AssignmentId,0,now));Assert.Throws<WarehouseCoordinationConflictException>(()=>a.Renew(owner,a.AssignmentId,1,now.AddMinutes(15)));a.Renew(owner,a.AssignmentId,1,now.AddMinutes(1));Assert.Equal(now.AddMinutes(16),a.ExpiresAt);}
}
