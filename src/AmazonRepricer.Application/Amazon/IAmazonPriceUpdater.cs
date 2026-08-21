namespace AmazonRepricer.Application.Amazon;

public interface IAmazonPriceUpdater
{
    Task<AmazonPriceUpdateResult> UpdatePriceAsync(
        string sellerId,
        string sku,
        string marketplaceId,
        string productType,
        decimal price,
        string currencyCode,
        CancellationToken cancellationToken = default);
}
