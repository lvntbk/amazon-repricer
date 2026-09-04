using System.Net.Http.Json;
using AmazonRepricer.Application.Amazon;

namespace AmazonRepricer.Infrastructure.Amazon;

public sealed class AmazonListingsPriceUpdater
    : IAmazonPriceUpdater
{
    private readonly HttpClient _httpClient;
    private readonly ILwaAccessTokenProvider _accessTokenProvider;

    public AmazonListingsPriceUpdater(
        HttpClient httpClient,
        ILwaAccessTokenProvider accessTokenProvider)
    {
        _httpClient = httpClient;
        _accessTokenProvider = accessTokenProvider;
    }

    public async Task<AmazonPriceUpdateResult> UpdatePriceAsync(
        string sellerId,
        string sku,
        string marketplaceId,
        string productType,
        decimal price,
        string currencyCode,
        CancellationToken cancellationToken = default)
    {
        Validate(
            sellerId,
            sku,
            marketplaceId,
            productType,
            price,
            currencyCode);

        var accessToken =
            await _accessTokenProvider.GetAccessTokenAsync(
                cancellationToken);

        var encodedSellerId =
            Uri.EscapeDataString(sellerId);

        var encodedSku =
            Uri.EscapeDataString(sku);

        var encodedMarketplaceId =
            Uri.EscapeDataString(marketplaceId);

        var path =
            $"listings/2021-08-01/items/" +
            $"{encodedSellerId}/{encodedSku}" +
            $"?marketplaceIds={encodedMarketplaceId}" +
            $"&issueLocale=tr_TR";

        var body = new
        {
            productType,
            patches = new[]
            {
                new
                {
                    op = "replace",
                    path = "/attributes/purchasable_offer",
                    value = new[]
                    {
                        new
                        {
                            marketplace_id = marketplaceId,
                            currency = currencyCode,
                            our_price = new[]
                            {
                                new
                                {
                                    schedule = new[]
                                    {
                                        new
                                        {
                                            value_with_tax = price
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            path);

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

            if (error.Length > 1000)
                error = error[..1000];

            throw new HttpRequestException(
                $"Amazon listing price update failed with status " +
                $"{(int)response.StatusCode}. Response: {error}");
        }

        try
        {
            var result =
                await response.Content.ReadFromJsonAsync<
                    ListingsItemSubmissionResponse>(
                    cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    "Amazon listing update returned an empty response.");

            var issues = result.Issues
                .Select(x =>
                    $"{x.Severity}: {x.Code} - {x.Message}")
                .ToArray();

            var accepted = string.Equals(
                result.Status,
                "ACCEPTED",
                StringComparison.OrdinalIgnoreCase);

            return new AmazonPriceUpdateResult(
                accepted,
                string.IsNullOrWhiteSpace(result.SubmissionId)
                    ? null
                    : result.SubmissionId,
                issues);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new HttpRequestException(
                "Amazon listing price update response could not be " +
                "processed; outcome is uncertain.",
                exception);
        }
    }

    private static void Validate(
        string sellerId,
        string sku,
        string marketplaceId,
        string productType,
        decimal price,
        string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(sellerId))
            throw new ArgumentException("Seller ID is required.");

        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU is required.");

        if (string.IsNullOrWhiteSpace(marketplaceId))
            throw new ArgumentException("Marketplace ID is required.");

        if (string.IsNullOrWhiteSpace(productType))
            throw new ArgumentException("Product type is required.");

        if (price <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(price),
                "Price must be greater than zero.");

        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("Currency code is required.");
    }
}
