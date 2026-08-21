using System.Net;
using System.Text;
using AmazonRepricer.Infrastructure.Amazon;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Tests.Amazon;

public sealed class LwaAccessTokenProviderTests
{
    [Fact]
    public async Task GetAccessTokenAsync_ShouldFail_WhenClientIdIsMissing()
    {
        var provider = CreateProvider(
            new AmazonSpApiOptions
            {
                ClientId = "",
                ClientSecret = "test-secret",
                RefreshToken = "test-refresh-token"
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetAccessTokenAsync());

        Assert.Contains("ClientId", exception.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldFail_WhenClientSecretIsMissing()
    {
        var provider = CreateProvider(
            new AmazonSpApiOptions
            {
                ClientId = "test-client",
                ClientSecret = "",
                RefreshToken = "test-refresh-token"
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetAccessTokenAsync());

        Assert.Contains("ClientSecret", exception.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldFail_WhenRefreshTokenIsMissing()
    {
        var provider = CreateProvider(
            new AmazonSpApiOptions
            {
                ClientId = "test-client",
                ClientSecret = "test-secret",
                RefreshToken = ""
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetAccessTokenAsync());

        Assert.Contains("RefreshToken", exception.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldReturnToken_FromSuccessfulResponse()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """
            {
              "access_token": "test-access-token",
              "token_type": "bearer",
              "expires_in": 3600
            }
            """);

        var provider = CreateProvider(
            ValidOptions(),
            handler);

        var token = await provider.GetAccessTokenAsync();

        Assert.Equal("test-access-token", token);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldCacheValidToken()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """
            {
              "access_token": "cached-access-token",
              "token_type": "bearer",
              "expires_in": 3600
            }
            """);

        var provider = CreateProvider(
            ValidOptions(),
            handler);

        var firstToken = await provider.GetAccessTokenAsync();
        var secondToken = await provider.GetAccessTokenAsync();

        Assert.Equal("cached-access-token", firstToken);
        Assert.Equal(firstToken, secondToken);

        // The second call must use the cached token.
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldThrow_WhenAmazonReturnsError()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.BadRequest,
            """
            {
              "error": "invalid_grant",
              "error_description": "The request has an invalid grant parameter."
            }
            """);

        var provider = CreateProvider(
            ValidOptions(),
            handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GetAccessTokenAsync());

        Assert.Contains("400", exception.Message);
        Assert.DoesNotContain("invalid_grant", exception.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    private static AmazonSpApiOptions ValidOptions()
    {
        return new AmazonSpApiOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            RefreshToken = "test-refresh-token"
        };
    }

    private static LwaAccessTokenProvider CreateProvider(
        AmazonSpApiOptions options,
        HttpMessageHandler? handler = null)
    {
        var httpClient = handler is null
            ? new HttpClient()
            : new HttpClient(handler);

        httpClient.BaseAddress =
            new Uri("https://api.amazon.com/");

        return new LwaAccessTokenProvider(
            httpClient,
            Options.Create(options));
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public int RequestCount { get; private set; }

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
            RequestCount++;

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    _responseBody,
                    Encoding.UTF8,
                    "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
