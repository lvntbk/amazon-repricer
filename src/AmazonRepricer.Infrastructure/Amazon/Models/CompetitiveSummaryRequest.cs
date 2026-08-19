using System.Text.Json.Serialization;

namespace AmazonRepricer.Infrastructure.Amazon.Models;

public sealed class CompetitiveSummaryBatchRequest
{
    [JsonPropertyName("requests")]
    public List<CompetitiveSummaryRequest> Requests { get; set; } = [];
}

public sealed class CompetitiveSummaryRequest
{
    [JsonPropertyName("asin")]
    public string Asin { get; set; } = string.Empty;

    [JsonPropertyName("marketplaceId")]
    public string MarketplaceId { get; set; } = string.Empty;

    [JsonPropertyName("includedData")]
    public List<string> IncludedData { get; set; } =
    [
        "featuredBuyingOptions",
        "lowestPricedOffers"
    ];

    [JsonPropertyName("method")]
    public string Method { get; set; } = "GET";
}
