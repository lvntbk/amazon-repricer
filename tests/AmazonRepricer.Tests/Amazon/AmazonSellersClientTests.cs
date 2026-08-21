using System.Net;
using System.Text;
using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Infrastructure.Amazon.Sellers;

namespace AmazonRepricer.Tests.Amazon;

public sealed class AmazonSellersClientTests
{
    [Fact]
    public async Task GetMarketplaceParticipationsAsync_ShouldCallExpectedEndpoint()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """
            {
              "payload": []
            }
            """);

        var client = CreateClient(handler);

        await client.GetMarketplaceParticipationsAsync();

        Assert.Equal(
            HttpMethod.Get,
            handler.LastRequestMethod);

        Assert.Equal(
            "https://sandbox.sellingpartnerapi-eu.amazon.com/sellers/v1/marketplaceParticipations",
            handler.LastRequestUri?.ToString());

        Assert.Equal(
            "test-access-token",
            handler.LastAccessToken);
    }

    [Fact]
    public async Task GetMarketplaceParticipationsAsync_ShouldParseResponse()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """
            {
              "payload": [
                {
                  "marketplace": {
                    "id": "A33AVAJ2PDY3EV",
                    "countryCode": "TR",
                    "name": "Amazon.com.tr",
                    "defaultCurrencyCode": "TRY",
                    "defaultLanguageCode": "tr_TR",
                    "domainName": "www.amazon.com.tr"
                  },
                  "participation": {
                    "isParticipating": true,
                    "hasSuspendedListings": false
                  }
                }
              ]
            }
            """);

        var client = CreateClient(handler);

        var result =
            await client.GetMarketplaceParticipationsAsync();

        var marketplace = Assert.Single(result);

        Assert.Equal(
            "A33AVAJ2PDY3EV",
            marketplace.Marketplace.Id);

        Assert.Equal(
            "TR",
            marketplace.Marketplace.CountryCode);

        Assert.Equal(
            "TRY",
            marketplace.Marketplace.DefaultCurrencyCode);

        Assert.True(
            marketplace.Participation.IsParticipating);

        Assert.False(
            marketplace.Participation.HasSuspendedListings);
    }

    [Fact]
    public async Task GetMarketplaceParticipationsAsync_ShouldThrow_WhenAmazonFails()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.Unauthorized,
            """
            {
              "errors": [
                {
                  "code": "Unauthorized",
                  "message": "Access token is invalid."
                }
              ]
            }
            """);

        var client = CreateClient(handler);

        var exception =
            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetMarketplaceParticipationsAsync());

        Assert.Contains("401", exception.Message);
        Assert.Contains("Unauthorized", exception.Message);
    }

    private static AmazonSellersClient CreateClient(
        HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(
                "https://sandbox.sellingpartnerapi-eu.amazon.com/")
        };

        return new AmazonSellersClient(
            httpClient,
            new FakeAccessTokenProvider());
    }

    private sealed class FakeAccessTokenProvider
        : ILwaAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("test-access-token");
        }
    }

    private sealed class FakeHttpMessageHandler
        : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public HttpMethod? LastRequestMethod { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastAccessToken { get; private set; }

        public FakeHttpMessageHandler(
            HttpStatusCode statusCode,
            string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestMethod = request.Method;
            LastRequestUri = request.RequestUri;

            if (request.Headers.TryGetValues(
                    "x-amz-access-token",
                    out var values))
            {
                LastAccessToken = values.SingleOrDefault();
            }

            return Task.FromResult(
                new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(
                        _responseBody,
                        Encoding.UTF8,
                        "application/json")
                });
        }
    }
}
