namespace AmazonRepricer.Domain.Entities;

public sealed class RepricingSafetySettings
{
    public const int GlobalId = 1;

    public int Id { get; set; } = GlobalId;

    public bool PriceUpdatesEnabled { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
