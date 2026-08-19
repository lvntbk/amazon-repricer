using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Infrastructure.Amazon;

public sealed class LwaAccessTokenProvider : ILwaAccessTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly AmazonSpApiOptions _options;

    private string? _cachedAccessToken;
    private DateTimeOffset _expiresAtUtc;

    public LwaAccessTokenProvider(
        HttpClient httpClient,
        IOptions<AmazonSpApiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_cachedAccessToken) &&
            DateTimeOffset.UtcNow < _expiresAtUtc)
        {
            return _cachedAccessToken;
        }

        ValidateConfiguration();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "auth/o2/token");

        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _options.RefreshToken,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret
            });

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(
                cancellationToken);

            throw new HttpRequestException(
                $"Amazon LWA token request failed. " +
                $"Status: {(int)response.StatusCode}. Body: {error}");
        }

        var tokenResponse =
            await response.Content.ReadFromJsonAsync<LwaAccessTokenResponse>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "Amazon LWA returned an empty response.");

        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException(
                "Amazon LWA response did not contain an access token.");
        }

        _cachedAccessToken = tokenResponse.AccessToken;

        var safeLifetimeSeconds =
            Math.Max(tokenResponse.ExpiresIn - 60, 1);

        _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(
            safeLifetimeSeconds);

        return _cachedAccessToken;
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException(
                "Amazon SP-API ClientId is not configured.");

        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException(
                "Amazon SP-API ClientSecret is not configured.");

        if (string.IsNullOrWhiteSpace(_options.RefreshToken))
            throw new InvalidOperationException(
                "Amazon SP-API RefreshToken is not configured.");
    }
}
