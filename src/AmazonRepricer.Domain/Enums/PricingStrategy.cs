namespace AmazonRepricer.Domain.Enums;

public enum PricingStrategy
{
    MatchFeaturedOffer = 1,
    BelowFeaturedOfferByAmount = 2,
    BelowFeaturedOfferByPercentage = 3
}
