using AmazonRepricer.Domain.Entities;

namespace AmazonRepricer.Application.Pricing;

public interface IPricingEngine
{
    PricingResult Calculate(
        decimal currentPrice,
        decimal? featuredOfferPrice,
        bool isFeaturedOfferOurs,
        PricingRule rule);
}
