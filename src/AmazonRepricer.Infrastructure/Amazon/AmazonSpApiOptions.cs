namespace AmazonRepricer.Infrastructure.Amazon;

public sealed class AmazonSpApiOptions
{
    public const string SectionName = "AmazonSpApi";

    public bool UseMock { get; set; } = true;

    public string Endpoint { get; set; } = string.Empty;

    public string MarketplaceId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;
}
