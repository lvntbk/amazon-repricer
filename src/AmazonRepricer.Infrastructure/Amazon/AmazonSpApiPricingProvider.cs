using System.Net.Http.Json;
using AmazonRepricer.Application.Amazon;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Infrastructure.Amazon;

public sealed class AmazonSpApiPricingProvider : IAmazonPricingProvider
{
    private const string CompetitiveSummaryPath =
        "batches/products/pricing/2022-05-01/items/competitiveSummary";

    private readonly HttpClient _httpClient;
    private readonly ILwaAccessTokenProvider _accessTokenProvider;
    private readonly AmazonSpApiOptions _options;
    private readonly ILogger<AmazonSpApiPricingProvider> _logger;

    public AmazonSpApiPricingProvider(
        HttpClient httpClient,
        ILwaAccessTokenProvider accessTokenProvider,
        IOptions<AmazonSpApiOptions> options,
        ILogger<AmazonSpApiPricingProvider> logger)
    {
        _httpClient = httpClient;
        _accessTokenProvider = accessTokenProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AmazonPricingInfo> GetPricingAsync(
        string asin,
        string sku,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(asin))
            throw new ArgumentException("ASIN is required.", nameof(asin));

        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU is required.", nameof(sku));

        ValidateConfiguration();

        var accessToken =
            await _accessTokenProvider.GetAccessTokenAsync(cancellationToken);

        var body = new
        {
            requests = new[]
            {
                new
                {
                    asin,
                    marketplaceId = _options.MarketplaceId,
                    includedData = new[]
                    {
                        "featuredBuyingOptions"
                    },
                    uri =
                        "/products/pricing/2022-05-01/items/competitiveSummary",
                    method = "GET"
                }
            }
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CompetitiveSummaryPath);

        request.Headers.TryAddWithoutValidation(
            "x-amz-access-token",
            accessToken);

        request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(
                cancellationToken);

            throw new HttpRequestException(
                $"Amazon pricing request failed with status " +
                $"{(int)response.StatusCode}. Response: {error}");
        }

        var batch =
            await response.Content.ReadFromJsonAsync<
                CompetitiveSummaryBatchResponse>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "Amazon pricing API returned an empty response.");

        var item = batch.Responses.SingleOrDefault()
            ?? throw new InvalidOperationException(
                "Amazon pricing API returned no item response.");

        if (item.Status.StatusCode != 200)
        {
            throw new HttpRequestException(
                $"Amazon pricing item failed with status " +
                $"{item.Status.StatusCode}: {item.Status.ReasonPhrase}");
        }

        var featuredOffer = item.Body.FeaturedBuyingOptions
            .Where(x => string.Equals(
                x.BuyingOptionType,
                "New",
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.SegmentedFeaturedOffers)
            .Select(x => new
            {
                Offer = x,
                LandedPrice =
                    x.ListingPrice.Amount +
                    x.ShippingOptions
                        .Where(y => y.ShippingOptionType == "DEFAULT")
                        .Select(y => y.Price.Amount)
                        .FirstOrDefault()
            })
            .OrderBy(x => x.LandedPrice)
            .FirstOrDefault();

        if (featuredOffer is null)
        {
            return new AmazonPricingInfo(
                FeaturedOfferPrice: null,
                IsFeaturedOfferOurs: false);
        }

        var isOurs = string.Equals(
            featuredOffer.Offer.SellerId,
            _options.SellerId,
            StringComparison.Ordinal);

        _logger.LogInformation(
            "Amazon pricing received for SKU {Sku}, ASIN {Asin}: featured offer {Price}, ours {IsOurs}.",
            sku,
            asin,
            featuredOffer.LandedPrice,
            isOurs);

        return new AmazonPricingInfo(
            featuredOffer.LandedPrice,
            isOurs);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
            throw new InvalidOperationException(
                "AmazonSpApi:Endpoint is required.");

        if (string.IsNullOrWhiteSpace(_options.MarketplaceId))
            throw new InvalidOperationException(
                "AmazonSpApi:MarketplaceId is required.");

        if (string.IsNullOrWhiteSpace(_options.SellerId))
            throw new InvalidOperationException(
                "AmazonSpApi:SellerId is required.");
    }
}
