using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;

namespace AmazonRepricer.Tests.Repricing;

public sealed class RepricingEventTests
{
    [Fact]
    public void NewEvent_ShouldBePending()
    {
        var item = new RepricingEvent();

        Assert.Equal(RepricingStatus.Pending, item.Status);
        Assert.Null(item.ReviewedAtUtc);
    }

    [Fact]
    public void Approve_ShouldChangePendingEventToApproved()
    {
        var item = new RepricingEvent();

        item.Approve("  Price checked.  ");

        Assert.Equal(RepricingStatus.Approved, item.Status);
        Assert.Equal("Price checked.", item.ReviewNote);
        Assert.NotNull(item.ReviewedAtUtc);
    }

    [Fact]
    public void Reject_ShouldChangePendingEventToRejected()
    {
        var item = new RepricingEvent();

        item.Reject("Profit margin is too low.");

        Assert.Equal(RepricingStatus.Rejected, item.Status);
        Assert.Equal(
            "Profit margin is too low.",
            item.ReviewNote);
        Assert.NotNull(item.ReviewedAtUtc);
    }

    [Fact]
    public void Approve_ShouldFail_WhenEventWasAlreadyReviewed()
    {
        var item = new RepricingEvent();
        item.Reject();

        var exception = Assert.Throws<InvalidOperationException>(
            () => item.Approve());

        Assert.Contains("pending", exception.Message);
    }
}
