using System.Net.Http.Headers;
using AmazonRepricer.Application.Amazon;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Infrastructure.Amazon;

public sealed class AmazonSpApiPricingProvider : IAmazonPricingProvider
{
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

        var accessToken = await _accessTokenProvider.GetAccessTokenAsync(
            cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        request.Headers.TryAddWithoutValidation(
            "x-amz-access-token",
            accessToken);

        _logger.LogDebug(
            "SP-API pricing provider initialized for ASIN {Asin}, SKU {Sku}, Marketplace {MarketplaceId}.",
            asin,
            sku,
            _options.MarketplaceId);

        throw new NotSupportedException(
            "Amazon Product Pricing request contract has not been enabled yet.");
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
            throw new InvalidOperationException(
                "AmazonSpApi:Endpoint is required.");

        if (string.IsNullOrWhiteSpace(_options.MarketplaceId))
            throw new InvalidOperationException(
                "AmazonSpApi:MarketplaceId is required.");
    }
}
