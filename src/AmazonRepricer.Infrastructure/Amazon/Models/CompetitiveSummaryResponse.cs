using System.Text.Json.Serialization;

namespace AmazonRepricer.Infrastructure.Amazon.Models;

public sealed class CompetitiveSummaryBatchResponse
{
    [JsonPropertyName("responses")]
    public List<CompetitiveSummaryResponse> Responses { get; set; } = [];
}

public sealed class CompetitiveSummaryResponse
{
    [JsonPropertyName("status")]
    public CompetitiveSummaryStatus? Status { get; set; }

    [JsonPropertyName("body")]
    public CompetitiveSummaryBody? Body { get; set; }
}

public sealed class CompetitiveSummaryStatus
{
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    [JsonPropertyName("reasonPhrase")]
    public string? ReasonPhrase { get; set; }
}

public sealed class CompetitiveSummaryBody
{
    [JsonPropertyName("asin")]
    public string? Asin { get; set; }

    [JsonPropertyName("featuredBuyingOptions")]
    public List<FeaturedBuyingOption> FeaturedBuyingOptions { get; set; } = [];
}

public sealed class FeaturedBuyingOption
{
    [JsonPropertyName("listingPrice")]
    public MoneyType? ListingPrice { get; set; }

    [JsonPropertyName("sellerId")]
    public string? SellerId { get; set; }

    [JsonPropertyName("condition")]
    public string? Condition { get; set; }
}

public sealed class MoneyType
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; set; }
}
