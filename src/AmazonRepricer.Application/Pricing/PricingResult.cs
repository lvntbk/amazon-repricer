namespace AmazonRepricer.Application.Pricing;

public sealed record PricingResult(
    decimal ProposedPrice,
    bool ShouldChangePrice,
    string Reason);
