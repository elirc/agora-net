using Agora.Domain.Common;
using Agora.Domain.Entities;

namespace Agora.Tests.Unit;

public class ReviewTests
{
    private static Review NewReview(int rating = 4) =>
        new(Guid.NewGuid(), Guid.NewGuid(), rating, "Solid", "Does what it says.");

    [Fact]
    public void NewReview_StartsPending()
    {
        var review = NewReview();

        Assert.Equal(ReviewStatus.Pending, review.Status);
        Assert.Null(review.ModeratedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void RatingOutOfRange_Throws(int rating)
    {
        Assert.Throws<DomainException>(() => NewReview(rating));
    }

    [Fact]
    public void EmptyBody_Throws()
    {
        Assert.Throws<DomainException>(() =>
            new Review(Guid.NewGuid(), Guid.NewGuid(), 3, null, "   "));
    }

    [Fact]
    public void Approve_SetsStatusAndTimestamp()
    {
        var review = NewReview();
        var now = DateTimeOffset.UtcNow;

        review.Approve(now);

        Assert.Equal(ReviewStatus.Approved, review.Status);
        Assert.Equal(now, review.ModeratedAt);
    }

    [Fact]
    public void Reject_KeepsNote()
    {
        var review = NewReview();

        review.Reject("Spam link", DateTimeOffset.UtcNow);

        Assert.Equal(ReviewStatus.Rejected, review.Status);
        Assert.Equal("Spam link", review.ModerationNote);
    }

    [Fact]
    public void Edit_ResetsToPending_AndClearsModeration()
    {
        var review = NewReview();
        review.Approve(DateTimeOffset.UtcNow);

        review.Edit(2, "Changed my mind", "Broke after a week.");

        Assert.Equal(ReviewStatus.Pending, review.Status);
        Assert.Null(review.ModeratedAt);
        Assert.Null(review.ModerationNote);
        Assert.Equal(2, review.Rating);
        Assert.Equal("Broke after a week.", review.Body);
    }

    [Fact]
    public void Edit_InvalidRating_Throws()
    {
        var review = NewReview();

        Assert.Throws<DomainException>(() => review.Edit(9, null, "body"));
    }
}
