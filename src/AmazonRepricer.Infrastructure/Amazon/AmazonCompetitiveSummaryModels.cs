using System.Text.Json.Serialization;

namespace AmazonRepricer.Infrastructure.Amazon;

internal sealed class CompetitiveSummaryBatchResponse
{
    [JsonPropertyName("responses")]
    public List<CompetitiveSummaryResponse> Responses { get; set; } = [];
}

internal sealed class CompetitiveSummaryResponse
{
    [JsonPropertyName("status")]
    public CompetitiveSummaryStatus Status { get; set; } = new();

    [JsonPropertyName("body")]
    public CompetitiveSummaryBody Body { get; set; } = new();
}

internal sealed class CompetitiveSummaryStatus
{
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    [JsonPropertyName("reasonPhrase")]
    public string ReasonPhrase { get; set; } = string.Empty;
}

internal sealed class CompetitiveSummaryBody
{
    [JsonPropertyName("asin")]
    public string Asin { get; set; } = string.Empty;

    [JsonPropertyName("marketplaceId")]
    public string MarketplaceId { get; set; } = string.Empty;

    [JsonPropertyName("featuredBuyingOptions")]
    public List<FeaturedBuyingOption> FeaturedBuyingOptions { get; set; } = [];
}

internal sealed class FeaturedBuyingOption
{
    [JsonPropertyName("buyingOptionType")]
    public string BuyingOptionType { get; set; } = string.Empty;

    [JsonPropertyName("segmentedFeaturedOffers")]
    public List<SegmentedFeaturedOffer> SegmentedFeaturedOffers { get; set; } = [];
}

internal sealed class SegmentedFeaturedOffer
{
    [JsonPropertyName("sellerId")]
    public string SellerId { get; set; } = string.Empty;

    [JsonPropertyName("condition")]
    public string Condition { get; set; } = string.Empty;

    [JsonPropertyName("fulfillmentType")]
    public string FulfillmentType { get; set; } = string.Empty;

    [JsonPropertyName("listingPrice")]
    public AmazonMoney ListingPrice { get; set; } = new();

    [JsonPropertyName("shippingOptions")]
    public List<AmazonShippingOption> ShippingOptions { get; set; } = [];
}

internal sealed class AmazonShippingOption
{
    [JsonPropertyName("shippingOptionType")]
    public string ShippingOptionType { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public AmazonMoney Price { get; set; } = new();
}

internal sealed class AmazonMoney
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; set; } = string.Empty;
}
