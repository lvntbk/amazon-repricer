using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;

namespace AmazonRepricer.Tests.Application.Pricing;

public sealed class PriceSubmissionSafetyPolicyTests
{
    [Fact]
    public void Evaluate_RejectsPriceChangeAboveMaximumPercentage()
    {
        var rule = CreateRule(
            minimumPrice: 1m,
            maximumPrice: 1000m);

        var result =
            PriceSubmissionSafetyPolicy.Evaluate(
                currentPrice: 100m,
                proposedPrice: 111m,
                pricingRule: rule,
                maxPriceChangePercentage: 10m);

        Assert.False(result.IsAllowed);
        Assert.Contains(
            "maximum",
            result.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_AllowsPriceChangeExactlyAtMaximumPercentage()
    {
        var rule = CreateRule(
            minimumPrice: 1m,
            maximumPrice: 1000m);

        var result =
            PriceSubmissionSafetyPolicy.Evaluate(
                currentPrice: 100m,
                proposedPrice: 110m,
                pricingRule: rule,
                maxPriceChangePercentage: 10m);

        Assert.True(result.IsAllowed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Evaluate_RejectsInvalidMaximumPercentage(
        int maxPriceChangePercentage)
    {
        var rule = CreateRule(
            minimumPrice: 1m,
            maximumPrice: 1000m);

        var result =
            PriceSubmissionSafetyPolicy.Evaluate(
                currentPrice: 100m,
                proposedPrice: 101m,
                pricingRule: rule,
                maxPriceChangePercentage:
                    maxPriceChangePercentage);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_StillRejectsPriceOutsidePricingRuleBounds()
    {
        var rule = CreateRule(
            minimumPrice: 90m,
            maximumPrice: 110m);

        var result =
            PriceSubmissionSafetyPolicy.Evaluate(
                currentPrice: 100m,
                proposedPrice: 150m,
                pricingRule: rule,
                maxPriceChangePercentage: 100m);

        Assert.False(result.IsAllowed);
        Assert.Contains(
            "outside",
            result.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    private static PricingRule CreateRule(
        decimal minimumPrice,
        decimal maximumPrice)
    {
        return new PricingRule
        {
            Strategy =
                PricingStrategy.MatchFeaturedOffer,
            MinimumPrice = minimumPrice,
            MaximumPrice = maximumPrice,
            AdjustmentValue = 0m,
            IsActive = true
        };
    }
}
