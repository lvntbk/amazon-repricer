using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;

namespace AmazonRepricer.Application.Pricing;

public sealed class PricingEngine : IPricingEngine
{
    public PricingResult Calculate(
        decimal currentPrice,
        decimal? featuredOfferPrice,
        bool isFeaturedOfferOurs,
        PricingRule rule)
    {
        Validate(currentPrice, rule);

        if (!rule.IsActive)
        {
            return new PricingResult(
                currentPrice,
                false,
                "Pricing rule is inactive.");
        }

        if (featuredOfferPrice is null)
        {
            return new PricingResult(
                currentPrice,
                false,
                "Featured Offer price is unavailable.");
        }

        if (isFeaturedOfferOurs)
        {
            return new PricingResult(
                currentPrice,
                false,
                "Featured Offer is already ours.");
        }

        var targetPrice = rule.Strategy switch
        {
            PricingStrategy.MatchFeaturedOffer =>
                featuredOfferPrice.Value,

            PricingStrategy.BelowFeaturedOfferByAmount =>
                featuredOfferPrice.Value - rule.AdjustmentValue,

            PricingStrategy.BelowFeaturedOfferByPercentage =>
                featuredOfferPrice.Value * (1 - rule.AdjustmentValue / 100m),

            _ => throw new ArgumentOutOfRangeException(
                nameof(rule.Strategy),
                rule.Strategy,
                "Unsupported pricing strategy.")
        };

        targetPrice = Math.Round(
            targetPrice,
            2,
            MidpointRounding.AwayFromZero);

        targetPrice = Math.Clamp(
            targetPrice,
            rule.MinimumPrice,
            rule.MaximumPrice);

        if (targetPrice == currentPrice)
        {
            return new PricingResult(
                currentPrice,
                false,
                "Calculated price is already active.");
        }

        return new PricingResult(
            targetPrice,
            true,
            $"Price change proposed from {currentPrice:F2} to {targetPrice:F2}.");
    }

    private static void Validate(
        decimal currentPrice,
        PricingRule rule)
    {
        if (currentPrice <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(currentPrice),
                "Current price must be greater than zero.");

        if (rule.MinimumPrice <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(rule.MinimumPrice),
                "Minimum price must be greater than zero.");

        if (rule.MaximumPrice < rule.MinimumPrice)
            throw new ArgumentException(
                "Maximum price cannot be lower than minimum price.");

        if (rule.AdjustmentValue < 0)
            throw new ArgumentOutOfRangeException(
                nameof(rule.AdjustmentValue),
                "Adjustment value cannot be negative.");

        if (rule.Strategy == PricingStrategy.BelowFeaturedOfferByPercentage &&
            rule.AdjustmentValue > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rule.AdjustmentValue),
                "Percentage adjustment cannot exceed 100.");
        }
    }
}
