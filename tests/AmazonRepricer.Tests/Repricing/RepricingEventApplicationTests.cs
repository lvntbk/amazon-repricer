using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;

namespace AmazonRepricer.Tests.Repricing;

public sealed class RepricingEventApplicationTests
{
    [Fact]
    public void MarkApplied_ShouldCompleteAwaitingVerificationEvent()
    {
        var repricingEvent = CreateAwaitingVerificationEvent();

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
            "awaiting verification",
            exception.Message);
    }

    [Fact]
    public void MarkApplied_ShouldRejectNonPositivePrice()
    {
        var repricingEvent = CreateAwaitingVerificationEvent();

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

    [Fact]
    public void RecordAmazonSubmission_ShouldPersistNormalizedResult()
    {
        var repricingEvent = CreateApprovedEvent();

        repricingEvent.RecordAmazonSubmission(
            accepted: true,
            submissionId: " submission-001 ",
            issues: new[]
            {
                " warning: price validation ",
                " ",
                "info: accepted"
            });

        Assert.Equal(
            "submission-001",
            repricingEvent.AmazonSubmissionId);
        Assert.True(repricingEvent.AmazonSubmissionAccepted);
        Assert.Equal(
            "warning: price validation | info: accepted",
            repricingEvent.AmazonSubmissionIssues);
        Assert.NotNull(repricingEvent.SubmittedAtUtc);
        Assert.Null(repricingEvent.ReconciledAtUtc);
    }

    [Fact]
    public void RecordAmazonSubmission_ShouldRejectPendingEvent()
    {
        var repricingEvent = new RepricingEvent();

        var exception = Assert.Throws<InvalidOperationException>(
            () => repricingEvent.RecordAmazonSubmission(
                accepted: true,
                submissionId: "submission-001",
                issues: Array.Empty<string>()));

        Assert.Contains(
            "Only approved events",
            exception.Message);
    }

    [Fact]
    public void MarkReconciled_ShouldOnlyAcceptFinalizedEvent()
    {
        var pendingEvent = new RepricingEvent();

        Assert.Throws<InvalidOperationException>(
            pendingEvent.MarkReconciled);

        var appliedEvent = CreateApprovedEvent();
        appliedEvent.RecordAmazonSubmission(
            accepted: true,
            submissionId: "submission-001",
            issues: Array.Empty<string>());
        appliedEvent.BeginApplication();
        appliedEvent.MarkAwaitingVerification();
        appliedEvent.MarkApplied(appliedEvent.ProposedPrice);

        appliedEvent.MarkReconciled();

        Assert.NotNull(appliedEvent.ReconciledAtUtc);
    }

    [Fact]
    public void BeginApplication_ShouldMoveApprovedEventToApplying()
    {
        var repricingEvent = CreateApprovedEvent();

        repricingEvent.BeginApplication();

        Assert.Equal(
            RepricingStatus.Applying,
            repricingEvent.Status);
    }

    [Fact]
    public void BeginApplication_ShouldRejectPendingEvent()
    {
        var repricingEvent = new RepricingEvent();

        var exception = Assert.Throws<InvalidOperationException>(
            repricingEvent.BeginApplication);

        Assert.Contains(
            "Only approved events",
            exception.Message);
    }

    [Fact]
    public void AcceptedEvent_ShouldCompleteAfterAwaitingVerification()
    {
        var repricingEvent = CreateApprovedEvent();
        repricingEvent.BeginApplication();

        repricingEvent.RecordAmazonSubmission(
            accepted: true,
            submissionId: "submission-applying-001",
            issues: Array.Empty<string>());

        repricingEvent.MarkAwaitingVerification();
        repricingEvent.MarkApplied(
            repricingEvent.ProposedPrice);

        Assert.Equal(
            RepricingStatus.Applied,
            repricingEvent.Status);
        Assert.True(repricingEvent.WasApplied);
        Assert.Equal(
            "submission-applying-001",
            repricingEvent.AmazonSubmissionId);
        Assert.True(repricingEvent.AmazonSubmissionAccepted);
        Assert.NotNull(repricingEvent.SubmittedAtUtc);
        Assert.NotNull(repricingEvent.ProcessedAtUtc);
    }

    [Fact]
    public void ApplyingEvent_ShouldAllowRejectedCompletion()
    {
        var repricingEvent = CreateApprovedEvent();
        repricingEvent.BeginApplication();

        repricingEvent.RecordAmazonSubmission(
            accepted: false,
            submissionId: "submission-rejected-001",
            issues: new[] { "Price rejected." });

        repricingEvent.MarkFailed(
            "Amazon rejected the price update.");

        Assert.Equal(
            RepricingStatus.Failed,
            repricingEvent.Status);
        Assert.False(repricingEvent.WasApplied);
        Assert.Equal(
            "submission-rejected-001",
            repricingEvent.AmazonSubmissionId);
        Assert.False(repricingEvent.AmazonSubmissionAccepted);
        Assert.Equal(
            "Price rejected.",
            repricingEvent.AmazonSubmissionIssues);
        Assert.NotNull(repricingEvent.SubmittedAtUtc);
        Assert.NotNull(repricingEvent.ProcessedAtUtc);
    }

    [Theory]
    [InlineData(RepricingStatus.Pending)]
    [InlineData(RepricingStatus.Approved)]
    [InlineData(RepricingStatus.Applying)]
    [InlineData(RepricingStatus.Rejected)]
    [InlineData(RepricingStatus.Failed)]
    [InlineData(RepricingStatus.Applied)]
    public void MarkApplied_CannotBypassVerificationState(
        RepricingStatus status)
    {
        var item = new RepricingEvent
        {
            Status = status,
            ProposedPrice = 99m,
            AmazonSubmissionAccepted = true,
            SubmittedAtUtc = DateTime.UtcNow
        };

        Assert.Throws<InvalidOperationException>(
            () => item.MarkApplied(99m));

        Assert.Equal(status, item.Status);
        Assert.Null(item.AppliedPrice);
        Assert.Null(item.ProcessedAtUtc);
    }

    [Fact]
    public void MarkApplied_MismatchingPrice_KeepsEventWaiting()
    {
        var item = CreateAwaitingVerificationEvent();

        Assert.Throws<InvalidOperationException>(
            () => item.MarkApplied(item.ProposedPrice + 1m));

        Assert.Equal(RepricingStatus.AwaitingVerification, item.Status);
        Assert.False(item.WasApplied);
        Assert.Null(item.AppliedPrice);
        Assert.Null(item.ProcessedAtUtc);
    }

    [Fact]
    public void MarkApplied_MissingSubmissionTime_KeepsEventWaiting()
    {
        var item = CreateAwaitingVerificationEvent();
        item.SubmittedAtUtc = null;

        Assert.Throws<InvalidOperationException>(
            () => item.MarkApplied(item.ProposedPrice));

        Assert.Equal(RepricingStatus.AwaitingVerification, item.Status);
        Assert.False(item.WasApplied);
    }

    [Fact]
    public void MarkApplied_UnacceptedSubmission_KeepsEventWaiting()
    {
        var item = CreateAwaitingVerificationEvent();
        item.AmazonSubmissionAccepted = false;

        Assert.Throws<InvalidOperationException>(
            () => item.MarkApplied(item.ProposedPrice));

        Assert.Equal(RepricingStatus.AwaitingVerification, item.Status);
        Assert.False(item.WasApplied);
    }

    private static RepricingEvent CreateAwaitingVerificationEvent()
    {
        var item = CreateApprovedEvent();
        item.BeginApplication();
        item.RecordAmazonSubmission(
            true,
            "submission-verification-test",
            Array.Empty<string>());
        item.MarkAwaitingVerification();
        return item;
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
