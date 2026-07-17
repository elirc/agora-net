using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class ReturnRequestTests
{
    private static ReturnRequest NewRequest() => new()
    {
        Number = "RMA-TEST",
        OrderId = Guid.NewGuid(),
        Reason = ReturnReason.Damaged,
    };

    [Fact]
    public void NewRequest_StartsRequested()
    {
        Assert.Equal(ReturnStatus.Requested, NewRequest().Status);
    }

    [Fact]
    public void Approve_RecordsTransactionAndTimestamp()
    {
        var request = NewRequest();
        var now = DateTimeOffset.UtcNow;

        request.Approve("rfnd_123", now);

        Assert.Equal(ReturnStatus.Approved, request.Status);
        Assert.Equal("rfnd_123", request.RefundTransactionId);
        Assert.Equal(now, request.ProcessedAt);
    }

    [Fact]
    public void Reject_KeepsNote()
    {
        var request = NewRequest();

        request.Reject("Outside window", DateTimeOffset.UtcNow);

        Assert.Equal(ReturnStatus.Rejected, request.Status);
        Assert.Equal("Outside window", request.RejectionNote);
    }

    [Fact]
    public void Cancel_FromRequested_Succeeds()
    {
        var request = NewRequest();

        request.Cancel(DateTimeOffset.UtcNow);

        Assert.Equal(ReturnStatus.Cancelled, request.Status);
    }

    [Fact]
    public void Approve_AfterReject_Throws()
    {
        var request = NewRequest();
        request.Reject(null, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidReturnStateException>(() =>
            request.Approve("rfnd_x", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Reject_AfterApprove_Throws()
    {
        var request = NewRequest();
        request.Approve("rfnd_x", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidReturnStateException>(() =>
            request.Reject(null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Cancel_AfterApprove_Throws()
    {
        var request = NewRequest();
        request.Approve("rfnd_x", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidReturnStateException>(() =>
            request.Cancel(DateTimeOffset.UtcNow));
    }
}
