namespace AmazonRepricer.Api.Contracts.Repricing;

public sealed record EvaluateRepricingRequest(
    Guid ProductId,
    decimal? FeaturedOfferPrice,
    bool IsFeaturedOfferOurs);
