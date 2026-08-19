namespace AmazonRepricer.Api.Contracts.Stores;

public sealed record CreateAmazonStoreRequest(
    string Name,
    string SellerId,
    string MarketplaceId);
