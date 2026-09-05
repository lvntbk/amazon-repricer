using AmazonRepricer.Domain.Entities;

namespace AmazonRepricer.Application.Pricing;

public sealed record PriceSubmissionSafetyResult(
    bool IsAllowed,
    string Reason);

public static class PriceSubmissionSafetyPolicy
{
    public static PriceSubmissionSafetyResult EvaluateHardBounds(
        decimal currentPrice,
        decimal proposedPrice,
        PricingRule? pricingRule)
    {
        if (currentPrice <= 0)
        {
            return Reject(
                "Current price must be greater than zero.");
        }

        if (proposedPrice <= 0)
        {
            return Reject(
                "Proposed price must be greater than zero.");
        }

        if (pricingRule is null)
        {
            return Reject(
                "An active pricing rule is required.");
        }

        if (!pricingRule.IsActive)
        {
            return Reject(
                "Pricing rule is inactive.");
        }

        if (pricingRule.MinimumPrice <= 0 ||
            pricingRule.MaximumPrice < pricingRule.MinimumPrice)
        {
            return Reject(
                "Pricing rule price boundaries are invalid.");
        }

        if (proposedPrice < pricingRule.MinimumPrice ||
            proposedPrice > pricingRule.MaximumPrice)
        {
            return Reject(
                $"Proposed price {proposedPrice:F2} is outside the " +
                $"allowed range {pricingRule.MinimumPrice:F2}-" +
                $"{pricingRule.MaximumPrice:F2}.");
        }

        return new PriceSubmissionSafetyResult(
            true,
            "Hard price boundaries passed.");
    }

    private static PriceSubmissionSafetyResult Reject(
        string reason) =>
        new(false, reason);
}
