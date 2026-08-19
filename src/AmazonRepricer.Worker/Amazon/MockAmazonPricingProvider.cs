using AmazonRepricer.Application.Amazon;

namespace AmazonRepricer.Worker.Amazon;

public sealed class MockAmazonPricingProvider : IAmazonPricingProvider
{
    private readonly ILogger<MockAmazonPricingProvider> _logger;

    public MockAmazonPricingProvider(
        ILogger<MockAmazonPricingProvider> logger)
    {
        _logger = logger;
    }

    public Task<AmazonPricingInfo> GetPricingAsync(
        string asin,
        string sku,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Mock Amazon pricing requested for ASIN {Asin}, SKU {Sku}",
            asin,
            sku);

        return Task.FromResult(
            new AmazonPricingInfo(
                FeaturedOfferPrice: 650m,
                IsFeaturedOfferOurs: false));
    }
}
