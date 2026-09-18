namespace AmazonRepricer.Application.Amazon;

public sealed record AmazonPricingInfo(
    decimal? FeaturedOfferPrice,
    bool IsFeaturedOfferOurs)
{
    public IReadOnlyList<AmazonCompetitiveOffer> CompetitiveOffers { get; init; }
        = Array.Empty<AmazonCompetitiveOffer>();
}
