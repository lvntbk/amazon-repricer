namespace AmazonRepricer.Application.Amazon;

public interface IAmazonListingReader
{
    Task<AmazonListingObservation> GetListingAsync(
        string sellerId,
        string sku,
        string marketplaceId,
        CancellationToken cancellationToken = default);
}
