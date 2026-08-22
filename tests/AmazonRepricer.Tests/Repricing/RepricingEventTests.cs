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
    public void ApproveAutomatically_ShouldApprovePendingEvent()
    {
        var item = new RepricingEvent();

        item.ApproveAutomatically(
            "Automatic repricing safety guard approved.");

        Assert.Equal(RepricingStatus.Approved, item.Status);
        Assert.Equal(
            "Automatic repricing safety guard approved.",
            item.ReviewNote);
        Assert.NotNull(item.ReviewedAtUtc);
    }

    [Fact]
    public void ApproveAutomatically_ShouldNormalizeReason()
    {
        var item = new RepricingEvent();

        item.ApproveAutomatically(
            "  Automatic repricing approved.  ");

        Assert.Equal(
            "Automatic repricing approved.",
            item.ReviewNote);
    }

    [Fact]
    public void ApproveAutomatically_ShouldFail_WhenReasonIsEmpty()
    {
        var item = new RepricingEvent();

        var exception = Assert.Throws<ArgumentException>(
            () => item.ApproveAutomatically("   "));

        Assert.Contains(
            "reason",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(RepricingStatus.Pending, item.Status);
        Assert.Null(item.ReviewedAtUtc);
    }

    [Fact]
    public void ApproveAutomatically_ShouldFail_WhenEventWasAlreadyReviewed()
    {
        var item = new RepricingEvent();

        item.Reject("Rejected manually.");

        var exception = Assert.Throws<InvalidOperationException>(
            () => item.ApproveAutomatically(
                "Automatic repricing approved."));

        Assert.Contains(
            "pending",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(RepricingStatus.Rejected, item.Status);
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
