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
    public void ApplyingEvent_ShouldAllowAcceptedCompletion()
    {
        var repricingEvent = CreateApprovedEvent();
        repricingEvent.BeginApplication();

        repricingEvent.RecordAmazonSubmission(
            accepted: true,
            submissionId: "submission-applying-001",
            issues: Array.Empty<string>());

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
