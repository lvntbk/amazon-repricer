using System.Net;
using System.Text;
using AmazonRepricer.Infrastructure.Amazon;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Tests.Amazon;

public sealed class AmazonSpApiPricingProviderTests
{
    [Fact]
    public async Task GetPricingAsync_ShouldReturnLandedFeaturedOfferPrice()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            SuccessfulResponse(
                sellerId: "COMPETITOR-001",
                listingPrice: 1099.90m,
                shippingPrice: 15m));

        var provider = CreateProvider(handler);

        var result = await provider.GetPricingAsync(
            "B0TEST0001",
            "TEST-SKU-001",
            CancellationToken.None);

        Assert.Equal(1114.90m, result.FeaturedOfferPrice);
        Assert.False(result.IsFeaturedOfferOurs);

        Assert.Equal(
            "https://sandbox.test/batches/products/pricing/2022-05-01/items/competitiveSummary",
            handler.RequestUri?.ToString());

        Assert.Equal(
            "local-access-token",
            handler.AccessToken);

        Assert.Contains(
            "\"featuredBuyingOptions\"",
            handler.RequestBody);
    }

    [Fact]
    public async Task GetPricingAsync_ShouldDetectOurFeaturedOffer()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            SuccessfulResponse(
                sellerId: "LOCAL-SELLER-001",
                listingPrice: 1000m,
                shippingPrice: 0m));

        var provider = CreateProvider(handler);

        var result = await provider.GetPricingAsync(
            "B0TEST0001",
            "TEST-SKU-001",
            CancellationToken.None);

        Assert.Equal(1000m, result.FeaturedOfferPrice);
        Assert.True(result.IsFeaturedOfferOurs);
    }

    [Fact]
    public async Task GetPricingAsync_ShouldReturnNull_WhenFeaturedOfferIsMissing()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """
            {
              "responses": [
                {
                  "status": {
                    "statusCode": 200,
                    "reasonPhrase": "Success"
                  },
                  "body": {
                    "asin": "B0TEST0001",
                    "marketplaceId": "A33AVAJ2PDY3EV",
                    "featuredBuyingOptions": []
                  }
                }
              ]
            }
            """);

        var provider = CreateProvider(handler);

        var result = await provider.GetPricingAsync(
            "B0TEST0001",
            "TEST-SKU-001",
            CancellationToken.None);

        Assert.Null(result.FeaturedOfferPrice);
        Assert.False(result.IsFeaturedOfferOurs);
    }

    private static AmazonSpApiPricingProvider CreateProvider(
        HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://sandbox.test/")
        };

        var options = Options.Create(
            new AmazonSpApiOptions
            {
                Endpoint = "https://sandbox.test",
                MarketplaceId = "A33AVAJ2PDY3EV",
                SellerId = "LOCAL-SELLER-001"
            });

        return new AmazonSpApiPricingProvider(
            httpClient,
            new FakeAccessTokenProvider(),
            options,
            NullLogger<AmazonSpApiPricingProvider>.Instance);
    }

    private static string SuccessfulResponse(
        string sellerId,
        decimal listingPrice,
        decimal shippingPrice)
    {
        return $$"""
        {
          "responses": [
            {
              "status": {
                "statusCode": 200,
                "reasonPhrase": "Success"
              },
              "body": {
                "asin": "B0TEST0001",
                "marketplaceId": "A33AVAJ2PDY3EV",
                "featuredBuyingOptions": [
                  {
                    "buyingOptionType": "New",
                    "segmentedFeaturedOffers": [
                      {
                        "sellerId": "{{sellerId}}",
                        "condition": "New",
                        "fulfillmentType": "AFN",
                        "listingPrice": {
                          "amount": {{listingPrice}},
                          "currencyCode": "TRY"
                        },
                        "shippingOptions": [
                          {
                            "shippingOptionType": "DEFAULT",
                            "price": {
                              "amount": {{shippingPrice}},
                              "currencyCode": "TRY"
                            }
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
          ]
        }
        """;
    }

    private sealed class FakeAccessTokenProvider
        : ILwaAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("local-access-token");
        }
    }

    private sealed class FakeHttpMessageHandler
        : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _response;

        public Uri? RequestUri { get; private set; }
        public string? AccessToken { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        public FakeHttpMessageHandler(
            HttpStatusCode statusCode,
            string response)
        {
            _statusCode = statusCode;
            _response = response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;

            if (request.Headers.TryGetValues(
                    "x-amz-access-token",
                    out var values))
            {
                AccessToken = values.SingleOrDefault();
            }

            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    _response,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
