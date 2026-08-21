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

    public void MarkApplied(decimal appliedPrice)
    {
        EnsureApproved();

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
        EnsureApproved();

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
