using System.Text.Json.Serialization;

namespace AmazonRepricer.Infrastructure.Amazon.Sellers;

public sealed class AmazonMarketplaceParticipation
{
    [JsonPropertyName("marketplace")]
    public AmazonMarketplace Marketplace { get; set; } = new();

    [JsonPropertyName("participation")]
    public AmazonParticipation Participation { get; set; } = new();
}

public sealed class AmazonMarketplace
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("defaultCurrencyCode")]
    public string DefaultCurrencyCode { get; set; } = string.Empty;

    [JsonPropertyName("defaultLanguageCode")]
    public string DefaultLanguageCode { get; set; } = string.Empty;

    [JsonPropertyName("domainName")]
    public string DomainName { get; set; } = string.Empty;
}

public sealed class AmazonParticipation
{
    [JsonPropertyName("isParticipating")]
    public bool IsParticipating { get; set; }

    [JsonPropertyName("hasSuspendedListings")]
    public bool HasSuspendedListings { get; set; }
}
