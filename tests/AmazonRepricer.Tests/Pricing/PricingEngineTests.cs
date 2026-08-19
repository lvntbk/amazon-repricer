using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;

namespace AmazonRepricer.Tests.Pricing;

public sealed class PricingEngineTests
{
    private readonly PricingEngine _engine = new();

    [Fact]
    public void BelowByAmount_ShouldUndercutFeaturedOffer()
    {
        var rule = CreateRule(
            PricingStrategy.BelowFeaturedOfferByAmount,
            minimum: 900m,
            maximum: 1500m,
            adjustment: 1m);

        var result = _engine.Calculate(
            currentPrice: 1100m,
            featuredOfferPrice: 1000m,
            isFeaturedOfferOurs: false,
            rule);

        Assert.True(result.ShouldChangePrice);
        Assert.Equal(999m, result.ProposedPrice);
    }

    [Fact]
    public void Price_ShouldNeverFallBelowMinimum()
    {
        var rule = CreateRule(
            PricingStrategy.BelowFeaturedOfferByAmount,
            minimum: 900m,
            maximum: 1500m,
            adjustment: 1m);

        var result = _engine.Calculate(
            currentPrice: 1000m,
            featuredOfferPrice: 850m,
            isFeaturedOfferOurs: false,
            rule);

        Assert.True(result.ShouldChangePrice);
        Assert.Equal(900m, result.ProposedPrice);
    }

    [Fact]
    public void Price_ShouldNeverExceedMaximum()
    {
        var rule = CreateRule(
            PricingStrategy.MatchFeaturedOffer,
            minimum: 900m,
            maximum: 1200m,
            adjustment: 0m);

        var result = _engine.Calculate(
            currentPrice: 1000m,
            featuredOfferPrice: 1500m,
            isFeaturedOfferOurs: false,
            rule);

        Assert.True(result.ShouldChangePrice);
        Assert.Equal(1200m, result.ProposedPrice);
    }

    [Fact]
    public void PercentageStrategy_ShouldCalculateCorrectly()
    {
        var rule = CreateRule(
            PricingStrategy.BelowFeaturedOfferByPercentage,
            minimum: 500m,
            maximum: 1500m,
            adjustment: 5m);

        var result = _engine.Calculate(
            currentPrice: 1100m,
            featuredOfferPrice: 1000m,
            isFeaturedOfferOurs: false,
            rule);

        Assert.Equal(950m, result.ProposedPrice);
    }

    [Fact]
    public void ShouldNotLowerPrice_WhenFeaturedOfferIsAlreadyOurs()
    {
        var rule = CreateRule(
            PricingStrategy.BelowFeaturedOfferByAmount,
            minimum: 900m,
            maximum: 1500m,
            adjustment: 1m);

        var result = _engine.Calculate(
            currentPrice: 1000m,
            featuredOfferPrice: 1000m,
            isFeaturedOfferOurs: true,
            rule);

        Assert.False(result.ShouldChangePrice);
        Assert.Equal(1000m, result.ProposedPrice);
    }

    [Fact]
    public void ShouldNotChangePrice_WhenFeaturedOfferIsUnavailable()
    {
        var rule = CreateRule(
            PricingStrategy.BelowFeaturedOfferByAmount,
            minimum: 900m,
            maximum: 1500m,
            adjustment: 1m);

        var result = _engine.Calculate(
            currentPrice: 1000m,
            featuredOfferPrice: null,
            isFeaturedOfferOurs: false,
            rule);

        Assert.False(result.ShouldChangePrice);
        Assert.Equal(1000m, result.ProposedPrice);
    }

    private static PricingRule CreateRule(
        PricingStrategy strategy,
        decimal minimum,
        decimal maximum,
        decimal adjustment)
    {
        return new PricingRule
        {
            Strategy = strategy,
            MinimumPrice = minimum,
            MaximumPrice = maximum,
            AdjustmentValue = adjustment,
            IsActive = true
        };
    }
}
