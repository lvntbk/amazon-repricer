namespace AmazonRepricer.Infrastructure.Amazon;

public sealed class AmazonSpApiOptions
{
    public const string SectionName = "AmazonSpApi";

    public bool UseMock { get; set; } = true;

    public string Environment { get; set; } = "Production";

    public string Endpoint { get; set; } = string.Empty;

    public string LwaEndpoint { get; set; } =
        "https://api.amazon.com";

    public string MarketplaceId { get; set; } = string.Empty;

    public string SellerId { get; set; } = string.Empty;

    public string DefaultProductType { get; set; } = "PRODUCT";

    public string CurrencyCode { get; set; } = "TRY";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;
}
