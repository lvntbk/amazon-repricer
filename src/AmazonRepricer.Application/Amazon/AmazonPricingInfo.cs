namespace AmazonRepricer.Application.Amazon;

public sealed record AmazonPricingInfo(
    decimal? FeaturedOfferPrice,
    bool IsFeaturedOfferOurs);
