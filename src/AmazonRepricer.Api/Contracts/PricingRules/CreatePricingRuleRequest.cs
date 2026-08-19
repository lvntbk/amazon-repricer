using AmazonRepricer.Domain.Enums;

namespace AmazonRepricer.Api.Contracts.PricingRules;

public sealed record CreatePricingRuleRequest(
    Guid ProductId,
    PricingStrategy Strategy,
    decimal MinimumPrice,
    decimal MaximumPrice,
    decimal AdjustmentValue,
    decimal? MinimumProfitPercentage);
