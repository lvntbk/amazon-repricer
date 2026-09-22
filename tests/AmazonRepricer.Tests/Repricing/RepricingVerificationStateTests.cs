using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;

namespace AmazonRepricer.Tests.Repricing;

public sealed class RepricingVerificationStateTests
{
    [Fact]
    public void AcceptedSubmission_AwaitsVerification_WithoutBeingApplied()
    {
        var item = CreateApplyingEvent();
        item.RecordAmazonSubmission(
            true,
            "submission-123",
            Array.Empty<string>());

        item.MarkAwaitingVerification();

        Assert.Equal(
            RepricingStatus.AwaitingVerification,
            item.Status);
        Assert.False(item.WasApplied);
        Assert.Null(item.AppliedPrice);
        Assert.Null(item.ProcessedAtUtc);
        Assert.Null(item.ReconciledAtUtc);
        Assert.Equal("submission-123", item.AmazonSubmissionId);
        Assert.NotNull(item.SubmittedAtUtc);
    }

    [Fact]
    public void MissingSubmission_CannotAwaitVerification()
    {
        var item = CreateApplyingEvent();

        Assert.Throws<InvalidOperationException>(
            () => item.MarkAwaitingVerification());

        Assert.Equal(RepricingStatus.Applying, item.Status);
    }

    [Fact]
    public void RejectedSubmission_CannotAwaitVerification()
    {
        var item = CreateApplyingEvent();
        item.RecordAmazonSubmission(
            false,
            null,
            new[] { "Submission rejected." });

        Assert.Throws<InvalidOperationException>(
            () => item.MarkAwaitingVerification());

        Assert.Equal(RepricingStatus.Applying, item.Status);
    }

    [Theory]
    [InlineData(RepricingStatus.Pending)]
    [InlineData(RepricingStatus.Approved)]
    [InlineData(RepricingStatus.Rejected)]
    [InlineData(RepricingStatus.Applied)]
    [InlineData(RepricingStatus.Failed)]
    [InlineData(RepricingStatus.AwaitingVerification)]
    public void NonApplyingEvent_CannotAwaitVerification(
        RepricingStatus status)
    {
        var item = new RepricingEvent
        {
            Status = status,
            AmazonSubmissionAccepted = true
        };

        Assert.Throws<InvalidOperationException>(
            () => item.MarkAwaitingVerification());

        Assert.Equal(status, item.Status);
    }

    private static RepricingEvent CreateApplyingEvent()
    {
        var item = new RepricingEvent
        {
            OldPrice = 100m,
            ProposedPrice = 99m
        };

        item.Approve("Verification state test");
        item.BeginApplication();

        return item;
    }
}
