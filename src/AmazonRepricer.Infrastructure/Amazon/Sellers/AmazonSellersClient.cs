using System.Net.Http.Json;
using System.Text.Json;

namespace AmazonRepricer.Infrastructure.Amazon.Sellers;

public sealed class AmazonSellersClient : IAmazonSellersClient
{
    private readonly HttpClient _httpClient;
    private readonly ILwaAccessTokenProvider _accessTokenProvider;

    public AmazonSellersClient(
        HttpClient httpClient,
        ILwaAccessTokenProvider accessTokenProvider)
    {
        _httpClient = httpClient;
        _accessTokenProvider = accessTokenProvider;
    }

    public async Task<IReadOnlyList<AmazonMarketplaceParticipation>>
        GetMarketplaceParticipationsAsync(
            CancellationToken cancellationToken = default)
    {
        var accessToken =
            await _accessTokenProvider.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "sellers/v1/marketplaceParticipations");

        request.Headers.TryAddWithoutValidation(
            "x-amz-access-token",
            accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(
                cancellationToken);

            if (error.Length > 1000)
                error = error[..1000];

            throw new HttpRequestException(
                $"Amazon Sellers API request failed with status " +
                $"{(int)response.StatusCode}. Response: {error}");
        }

        var result =
            await response.Content.ReadFromJsonAsync<
                AmazonMarketplaceParticipationsResponse>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Amazon Sellers API returned an empty response.");

        return result.Payload;
    }
}
