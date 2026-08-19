namespace AmazonRepricer.Domain.Entities;

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AmazonStoreId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string Asin { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public decimal? Cost { get; set; }

    public decimal? CurrentPrice { get; set; }

    public bool IsRepricingEnabled { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public AmazonStore AmazonStore { get; set; } = null!;

    public PricingRule? PricingRule { get; set; }

    public ICollection<PriceSnapshot> PriceSnapshots { get; set; }
        = new List<PriceSnapshot>();

    public ICollection<RepricingEvent> RepricingEvents { get; set; }
        = new List<RepricingEvent>();
}
