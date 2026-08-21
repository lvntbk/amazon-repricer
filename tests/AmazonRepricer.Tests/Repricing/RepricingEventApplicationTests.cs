using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;

namespace AmazonRepricer.Tests.Repricing;

public sealed class RepricingEventApplicationTests
{
    [Fact]
    public void MarkApplied_ShouldCompleteApprovedEvent()
    {
        var repricingEvent = CreateApprovedEvent();

        repricingEvent.MarkApplied(1098.90m);

        Assert.Equal(
            RepricingStatus.Applied,
            repricingEvent.Status);
        Assert.Equal(
            1098.90m,
            repricingEvent.AppliedPrice);
        Assert.True(repricingEvent.WasApplied);
        Assert.Null(repricingEvent.ApplicationError);
        Assert.NotNull(repricingEvent.ProcessedAtUtc);
    }

    [Fact]
    public void MarkApplied_ShouldRejectPendingEvent()
    {
        var repricingEvent = new RepricingEvent();

        var exception = Assert.Throws<InvalidOperationException>(
            () => repricingEvent.MarkApplied(1098.90m));

        Assert.Contains(
            "Only approved events",
            exception.Message);
    }

    [Fact]
    public void MarkApplied_ShouldRejectNonPositivePrice()
    {
        var repricingEvent = CreateApprovedEvent();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => repricingEvent.MarkApplied(0));
    }

    [Fact]
    public void MarkFailed_ShouldCompleteApprovedEvent()
    {
        var repricingEvent = CreateApprovedEvent();

        repricingEvent.MarkFailed(
            " Amazon submission was rejected. ");

        Assert.Equal(
            RepricingStatus.Failed,
            repricingEvent.Status);
        Assert.False(repricingEvent.WasApplied);
        Assert.Null(repricingEvent.AppliedPrice);
        Assert.Equal(
            "Amazon submission was rejected.",
            repricingEvent.ApplicationError);
        Assert.NotNull(repricingEvent.ProcessedAtUtc);
    }

    [Fact]
    public void MarkFailed_ShouldRejectEmptyError()
    {
        var repricingEvent = CreateApprovedEvent();

        Assert.Throws<ArgumentException>(
            () => repricingEvent.MarkFailed("   "));
    }

    private static RepricingEvent CreateApprovedEvent()
    {
        var repricingEvent = new RepricingEvent
        {
            OldPrice = 1100m,
            ProposedPrice = 1098.90m
        };

        repricingEvent.Approve("Price reviewed.");

        return repricingEvent;
    }
}
