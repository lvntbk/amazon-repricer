using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;

namespace AmazonRepricer.Application.Pricing;

public sealed class PricingEngine : IPricingEngine
{
    public PricingResult Calculate(
        decimal currentPrice,
        decimal? featuredOfferPrice,
        bool isFeaturedOfferOurs,
        PricingRule rule,
        decimal? cost = null)
    {
        Validate(currentPrice, rule, cost);

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

        var effectiveMinimumPrice = rule.MinimumPrice;

        if (cost.HasValue &&
            rule.MinimumProfitPercentage.HasValue)
        {
            var profitFloor = cost.Value *
                (1 + rule.MinimumProfitPercentage.Value / 100m);

            profitFloor = Math.Round(
                profitFloor,
                2,
                MidpointRounding.AwayFromZero);

            if (profitFloor > rule.MaximumPrice)
            {
                return new PricingResult(
                    currentPrice,
                    false,
                    $"Minimum profit floor {profitFloor:F2} exceeds maximum price {rule.MaximumPrice:F2}.");
            }

            effectiveMinimumPrice = Math.Max(
                effectiveMinimumPrice,
                profitFloor);
        }

        var targetPrice = rule.Strategy switch
        {
            PricingStrategy.MatchFeaturedOffer =>
                featuredOfferPrice.Value,

            PricingStrategy.BelowFeaturedOfferByAmount =>
                featuredOfferPrice.Value - rule.AdjustmentValue,

            PricingStrategy.BelowFeaturedOfferByPercentage =>
                featuredOfferPrice.Value *
                (1 - rule.AdjustmentValue / 100m),

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
            effectiveMinimumPrice,
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
        PricingRule rule,
        decimal? cost)
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

        if (cost.HasValue && cost.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cost),
                "Cost must be greater than zero.");
        }

        if (rule.MinimumProfitPercentage.HasValue &&
            rule.MinimumProfitPercentage.Value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rule.MinimumProfitPercentage),
                "Minimum profit percentage cannot be negative.");
        }
    }
}
