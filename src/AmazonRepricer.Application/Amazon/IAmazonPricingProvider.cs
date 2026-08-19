namespace AmazonRepricer.Application.Amazon;

public interface IAmazonPricingProvider
{
    Task<AmazonPricingInfo> GetPricingAsync(
        string asin,
        string sku,
        CancellationToken cancellationToken);
}
