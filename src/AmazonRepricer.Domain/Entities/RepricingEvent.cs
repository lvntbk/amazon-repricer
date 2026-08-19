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

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Product Product { get; set; } = null!;
}
