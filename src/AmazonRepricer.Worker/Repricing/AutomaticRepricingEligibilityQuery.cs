using AmazonRepricer.Domain.Entities;

namespace AmazonRepricer.Worker.Repricing;

public static class AutomaticRepricingEligibilityQuery
{
    public static IQueryable<Product> EligibleForAutomaticRepricing(
        this IQueryable<Product> products)
    {
        return products.Where(x =>
            x.IsRepricingEnabled &&
            x.PricingRule != null &&
            x.PricingRule.IsActive &&
            x.AmazonStore.IsActive &&
            x.AmazonStore.AutomaticRepricingEnabled);
    }
}
