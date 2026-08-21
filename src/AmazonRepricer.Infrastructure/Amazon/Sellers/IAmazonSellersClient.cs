namespace AmazonRepricer.Infrastructure.Amazon.Sellers;

public interface IAmazonSellersClient
{
    Task<IReadOnlyList<AmazonMarketplaceParticipation>>
        GetMarketplaceParticipationsAsync(
            CancellationToken cancellationToken = default);
}
