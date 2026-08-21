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
