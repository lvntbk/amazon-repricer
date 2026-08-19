namespace AmazonRepricer.Api.Contracts.Products;

public sealed record CreateProductRequest(
    Guid AmazonStoreId,
    string Sku,
    string Asin,
    string Title,
    decimal? Cost,
    decimal? CurrentPrice);
