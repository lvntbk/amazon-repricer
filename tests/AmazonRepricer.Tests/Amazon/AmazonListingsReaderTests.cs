using System.Net;
using System.Text;
using System.Text.Json;
using AmazonRepricer.Infrastructure.Amazon;

namespace AmazonRepricer.Tests.Amazon;

public sealed class AmazonListingsReaderTests
{
    [Theory]
    [InlineData("99.90")]
    [InlineData("\"99.90\"")]
    public async Task ReadsOffersPrice_AndSendsEncodedGet(string amountJson)
    {
        var body = $$"""
        {
          "sku": "SKU/A + 1",
          "attributes": {
            "purchasable_offer": [
              { "our_price": [{ "schedule": [{ "value_with_tax": 1 }] }] }
            ]
          },
          "offers": [{
            "marketplaceId": "MARKET-1",
            "offerType": "B2C",
            "price": { "amount": {{amountJson}}, "currencyCode": "TRY" }
          }],
          "issues": [{
            "code": "PRICE_WARNING",
            "severity": "WARNING",
            "message": "Review price.",
            "attributeNames": ["purchasable_offer"]
          }]
        }
        """;

        using var handler = new StubHandler(body);
        using var http = CreateHttpClient(handler);
        var reader = new AmazonListingsReader(http, new StubTokenProvider());

        var result = await reader.GetListingAsync(
            "SELLER-1", "SKU/A + 1", "MARKET-1");

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("test-token", handler.AccessToken);
        Assert.Contains(
            "SELLER-1/SKU%2FA%20%2B%201",
            handler.RequestUri!.AbsoluteUri);
        Assert.Contains("marketplaceIds=MARKET-1", handler.RequestUri.Query);
        Assert.Contains("includedData=offers,issues", handler.RequestUri.Query);

        Assert.Equal("SELLER-1", result.SellerId);
        Assert.Equal("SKU/A + 1", result.Sku);
        Assert.Equal("MARKET-1", result.MarketplaceId);
        Assert.True(result.ReadCompletedAtUtc >= result.ReadStartedAtUtc);

        var offer = Assert.Single(result.Offers);
        Assert.Equal(99.90m, offer.Price);
        Assert.Equal("TRY", offer.CurrencyCode);
        Assert.Equal("MARKET-1", offer.MarketplaceId);
        Assert.Equal("B2C", offer.OfferType);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("WARNING", issue.Severity);
        Assert.Contains("purchasable_offer", issue.AttributeNames);
    }

    [Theory]
    [InlineData("""{"sku":"OTHER","offers":[]}""")]
    [InlineData("""{"offers":[]}""")]
    public async Task WrongOrMissingSku_IsRejected(string body)
    {
        using var handler = new StubHandler(body);
        using var http = CreateHttpClient(handler);
        var reader = new AmazonListingsReader(http, new StubTokenProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.GetListingAsync("SELLER", "SKU", "MARKET"));
    }

    [Fact]
    public async Task MissingOffers_DoesNotUseSubmittedAttributesAsPrice()
    {
        const string body = """
        {
          "sku": "SKU",
          "attributes": {
            "purchasable_offer": [
              { "our_price": [{ "schedule": [{ "value_with_tax": 99 }] }] }
            ]
          }
        }
        """;

        using var handler = new StubHandler(body);
        using var http = CreateHttpClient(handler);
        var reader = new AmazonListingsReader(http, new StubTokenProvider());

        var result = await reader.GetListingAsync("SELLER", "SKU", "MARKET");

        Assert.Empty(result.Offers);
    }

    [Fact]
    public async Task PreservesOfferScope_AndDoesNotDefaultMissingAmountToZero()
    {
        const string body = """
        {
          "sku": "SKU",
          "offers": [
            {
              "marketplaceId": "OTHER-MARKET",
              "offerType": "B2B",
              "price": { "amount": "50", "currencyCode": "USD" }
            },
            {
              "marketplaceId": "MARKET",
              "offerType": "B2C",
              "price": { "currencyCode": "TRY" }
            }
          ]
        }
        """;

        using var handler = new StubHandler(body);
        using var http = CreateHttpClient(handler);
        var reader = new AmazonListingsReader(http, new StubTokenProvider());

        var result = await reader.GetListingAsync("SELLER", "SKU", "MARKET");

        Assert.Equal(2, result.Offers.Count);
        Assert.Equal("OTHER-MARKET", result.Offers[0].MarketplaceId);
        Assert.Equal("B2B", result.Offers[0].OfferType);
        Assert.Equal("USD", result.Offers[0].CurrencyCode);
        Assert.Null(result.Offers[1].Price);
    }

    [Fact]
    public async Task HttpFailure_DoesNotProduceAnObservation()
    {
        using var handler = new StubHandler("{}", HttpStatusCode.ServiceUnavailable);
        using var http = CreateHttpClient(handler);
        var reader = new AmazonListingsReader(http, new StubTokenProvider());

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => reader.GetListingAsync("SELLER", "SKU", "MARKET"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
    }

    [Fact]
    public async Task MalformedResponse_DoesNotProduceAnObservation()
    {
        using var handler = new StubHandler("{invalid");
        using var http = CreateHttpClient(handler);
        var reader = new AmazonListingsReader(http, new StubTokenProvider());

        await Assert.ThrowsAsync<JsonException>(
            () => reader.GetListingAsync("SELLER", "SKU", "MARKET"));
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://amazon.test/")
        };

    private sealed class StubTokenProvider : ILwaAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult("test-token");
    }

    private sealed class StubHandler(
        string body,
        HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AccessToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
                       Method = request.Method;
            RequestUri = request.RequestUri;
            AccessToken = request.Headers
                .GetValues("x-amz-access-token").Single();

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
