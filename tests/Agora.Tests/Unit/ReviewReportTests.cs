using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class ReviewReportTests
{
    [Theory]
    [InlineData(ReviewReportStatus.Resolved)]
    [InlineData(ReviewReportStatus.Dismissed)]
    public void Terminal_resolution_records_actor_time_and_revision_and_cannot_repeat(ReviewReportStatus outcome)
    {
        var report = new ReviewReport(Guid.NewGuid(), Guid.NewGuid(), ReviewReportReason.Abuse, "  comment  ", DateTimeOffset.UnixEpoch);
        var actor = Guid.NewGuid(); var time = DateTimeOffset.UnixEpoch.AddDays(1);
        Assert.Throws<DomainException>(() => report.Resolve(ReviewReportStatus.Open, null, actor, time));
        Assert.Throws<DomainException>(() => report.Resolve(outcome, new string('n', 501), actor, time));
        Assert.Equal(ReviewReportStatus.Open, report.Status); Assert.Equal(0, report.Version);
        report.Resolve(outcome, "  reviewed  ", actor, time);
        Assert.Equal((outcome, 1L, actor, time, "reviewed"), (report.Status, report.Version, report.ResolvedByAdminId!.Value, report.ResolvedAt!.Value, report.ResolutionNote));
        Assert.Throws<ReviewReportConflictException>(() => report.Resolve(outcome, null, actor, time));
    }
}
