namespace AmazonRepricer.Domain.Entities;

public sealed class PriceSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public decimal OurPrice { get; set; }

    public decimal? FeaturedOfferPrice { get; set; }

    public bool IsFeaturedOfferOurs { get; set; }

    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;

    public Product Product { get; set; } = null!;
}
