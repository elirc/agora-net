using Agora.Domain.Entities;
namespace Agora.Tests.Unit;
public sealed class ReportExportJobTests
{
 [Fact]public void Claim_lease_cancel_and_stale_publication_are_explicit(){var now=DateTimeOffset.UnixEpoch;var j=new ReportExportJob(Guid.NewGuid(),now,now.AddDays(1),now);var g=j.Claim(now);Assert.Equal(ReportExportStatus.Running,j.Status);Assert.Equal(now.AddMinutes(2),j.LeaseExpiresAt);j.Cancel(now.AddSeconds(1));Assert.False(j.Publish(g,now,now.AddSeconds(2)));Assert.Equal(ReportExportStatus.Running,j.Status);}
 [Fact]public void Publication_succeeds_only_for_current_unexpired_generation(){var now=DateTimeOffset.UnixEpoch;var j=new ReportExportJob(Guid.NewGuid(),now,now.AddDays(1),now);var g=j.Claim(now);Assert.False(j.Publish(g-1,now,now.AddSeconds(1)));Assert.True(j.Publish(g,now,now.AddSeconds(1)));Assert.Equal(now.AddHours(24).AddSeconds(1),j.ArtifactExpiresAt);}
 [Fact]public void Range_is_half_open_positive_and_at_most_ninety_days(){var now=DateTimeOffset.UnixEpoch;Assert.ThrowsAny<Exception>(()=>new ReportExportJob(Guid.NewGuid(),now,now,now));Assert.ThrowsAny<Exception>(()=>new ReportExportJob(Guid.NewGuid(),now,now.AddDays(90).AddTicks(1),now));}
 [Fact]public void Expired_leases_can_be_recovered_only_three_times(){var now=DateTimeOffset.UnixEpoch;var j=new ReportExportJob(Guid.NewGuid(),now,now.AddDays(1),now);Assert.Equal(1,j.Claim(now));Assert.Equal(2,j.Claim(now.AddMinutes(2)));Assert.Equal(3,j.Claim(now.AddMinutes(4)));j.Claim(now.AddMinutes(6));Assert.Equal(ReportExportStatus.Failed,j.Status);Assert.Equal("ClaimsExhausted",j.FailureCode);}
}
