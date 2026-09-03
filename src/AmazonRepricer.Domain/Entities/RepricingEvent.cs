using AmazonRepricer.Domain.Enums;

namespace AmazonRepricer.Domain.Entities;

public sealed class RepricingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public decimal OldPrice { get; set; }

    public decimal ProposedPrice { get; set; }

    public decimal? AppliedPrice { get; set; }

    public string Reason { get; set; } = string.Empty;

    public bool WasApplied { get; set; }

    public RepricingStatus Status { get; set; } =
        RepricingStatus.Pending;

    public string? ReviewNote { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    public string? ApplicationError { get; set; }

    public string? AmazonSubmissionId { get; set; }

    public bool? AmazonSubmissionAccepted { get; set; }

    public string? AmazonSubmissionIssues { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public DateTime? ReconciledAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Product Product { get; set; } = null!;

    public void Approve(string? note = null)
    {
        EnsurePending();

        Status = RepricingStatus.Approved;
        ReviewNote = NormalizeNote(note);
        ReviewedAtUtc = DateTime.UtcNow;
    }

    public void Reject(string? note = null)
    {
        EnsurePending();

        Status = RepricingStatus.Rejected;
        ReviewNote = NormalizeNote(note);
        ReviewedAtUtc = DateTime.UtcNow;
    }

    public void ApproveAutomatically(string reason)
    {
        EnsurePending();

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Automatic approval reason is required.",
                nameof(reason));
        }

        var normalizedReason = reason.Trim();

        if (normalizedReason.Length > 1000)
        {
            throw new ArgumentException(
                "Automatic approval reason cannot exceed 1000 characters.",
                nameof(reason));
        }

        Status = RepricingStatus.Approved;
        ReviewNote = normalizedReason;
        ReviewedAtUtc = DateTime.UtcNow;
    }

    public void BeginApplication()
    {
        EnsureApproved();

        Status = RepricingStatus.Applying;
    }

    public void RecordAmazonSubmission(
        bool accepted,
        string? submissionId,
        IEnumerable<string>? issues)
    {
        EnsureReadyForApplicationCompletion();

        var normalizedSubmissionId =
            string.IsNullOrWhiteSpace(submissionId)
                ? null
                : submissionId.Trim();

        if (normalizedSubmissionId?.Length > 200)
        {
            throw new ArgumentException(
                "Amazon submission ID cannot exceed 200 characters.",
                nameof(submissionId));
        }

        var normalizedIssues = issues?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToArray()
            ?? Array.Empty<string>();

        var joinedIssues = normalizedIssues.Length == 0
            ? null
            : string.Join(" | ", normalizedIssues);

        if (joinedIssues?.Length > 4000)
        {
            joinedIssues = joinedIssues[..4000];
        }

        AmazonSubmissionId = normalizedSubmissionId;
        AmazonSubmissionAccepted = accepted;
        AmazonSubmissionIssues = joinedIssues;
        SubmittedAtUtc = DateTime.UtcNow;
    }

    public void MarkReconciled()
    {
        if (Status != RepricingStatus.Applied &&
            Status != RepricingStatus.Failed)
        {
            throw new InvalidOperationException(
                "Only finalized events can be marked as reconciled.");
        }

        ReconciledAtUtc = DateTime.UtcNow;
    }

    public void MarkApplied(decimal appliedPrice)
    {
        EnsureReadyForApplicationCompletion();

        if (appliedPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(appliedPrice),
                "Applied price must be greater than zero.");
        }

        Status = RepricingStatus.Applied;
        AppliedPrice = appliedPrice;
        WasApplied = true;
        ApplicationError = null;
        ProcessedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string error)
    {
        EnsureReadyForApplicationCompletion();

        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException(
                "Application error is required.",
                nameof(error));
        }

        var normalizedError = error.Trim();

        if (normalizedError.Length > 1000)
        {
            throw new ArgumentException(
                "Application error cannot exceed 1000 characters.",
                nameof(error));
        }

        Status = RepricingStatus.Failed;
        AppliedPrice = null;
        WasApplied = false;
        ApplicationError = normalizedError;
        ProcessedAtUtc = DateTime.UtcNow;
    }

    private void EnsureReadyForApplicationCompletion()
    {
        if (Status != RepricingStatus.Approved &&
            Status != RepricingStatus.Applying)
        {
            throw new InvalidOperationException(
                $"Only approved events or events being applied " +
                $"can be processed. Current status: {Status}.");
        }
    }

    private void EnsureApproved()
    {
        if (Status != RepricingStatus.Approved)
        {
            throw new InvalidOperationException(
                $"Only approved events can be processed. " +
                $"Current status: {Status}.");
        }
    }

    private void EnsurePending()
    {
        if (Status != RepricingStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Only pending events can be reviewed. " +
                $"Current status: {Status}.");
        }
    }

    private static string? NormalizeNote(string? note)
    {
        return string.IsNullOrWhiteSpace(note)
            ? null
            : note.Trim();
    }
}
