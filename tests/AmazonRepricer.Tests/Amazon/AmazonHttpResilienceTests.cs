using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Infrastructure;
using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Infrastructure.Amazon.Sellers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AmazonRepricer.Tests.Amazon;

public sealed class AmazonHttpResilienceTests
{
    [Fact]
    public async Task PricingProvider_Retries429_ThenSucceeds()
    {
        var handler = new CountingHttpMessageHandler(
            attempt => attempt == 1
                ? TooManyRequests()
                : JsonResponse(
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
                    """));

        using var serviceProvider =
            CreateServiceProviderForPricingProvider(handler);

        var provider =
            serviceProvider.GetRequiredService<
                AmazonSpApiPricingProvider>();

        var result = await provider.GetPricingAsync(
            "B0TEST0001",
            "TEST-SKU-001",
            CancellationToken.None);

        Assert.Null(result.FeaturedOfferPrice);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task SellersClient_Retries429_ThenSucceeds()
    {
        var handler = new CountingHttpMessageHandler(
            attempt => attempt == 1
                ? TooManyRequests()
                : JsonResponse(
                    HttpStatusCode.OK,
                    """{"payload":[]}"""));

        using var serviceProvider =
            CreateServiceProviderForSellers(handler);

        var client =
            serviceProvider.GetRequiredService<IAmazonSellersClient>();

        var result =
            await client.GetMarketplaceParticipationsAsync();

        Assert.Empty(result);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task SellersClient_OpensCircuit_AfterRepeated429Responses()
    {
        var handler = new CountingHttpMessageHandler(
            _ => TooManyRequests());

        using var serviceProvider =
            CreateServiceProviderForSellers(handler);

        var client =
            serviceProvider.GetRequiredService<IAmazonSellersClient>();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetMarketplaceParticipationsAsync());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetMarketplaceParticipationsAsync());

        Assert.Equal(8, handler.RequestCount);

        var requestCountBeforeOpenCircuit =
            handler.RequestCount;

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.GetMarketplaceParticipationsAsync());

        Assert.Equal(
            requestCountBeforeOpenCircuit,
            handler.RequestCount);
    }

    [Fact]
    public async Task PriceUpdater_Retries429_ThenSucceeds()
    {
        var handler = new CountingHttpMessageHandler(
            attempt => attempt == 1
                ? TooManyRequests()
                : JsonResponse(
                    HttpStatusCode.Accepted,
                    """
                    {
                      "sku": "TEST-SKU-001",
                      "status": "ACCEPTED",
                      "submissionId": "submission-after-throttle",
                      "issues": []
                    }
                    """));

        using var serviceProvider =
            CreateServiceProviderForPriceUpdater(handler);

        var updater =
            serviceProvider.GetRequiredService<IAmazonPriceUpdater>();

        var result = await updater.UpdatePriceAsync(
            sellerId: "LOCAL-SELLER-001",
            sku: "TEST-SKU-001",
            marketplaceId: "A33AVAJ2PDY3EV",
            productType: "PRODUCT",
            price: 99m,
            currencyCode: "TRY");

        Assert.True(result.Accepted);
        Assert.Equal(
            "submission-after-throttle",
            result.SubmissionId);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task PriceUpdater_OpensCircuit_AfterRepeated429Responses()
    {
        var handler = new CountingHttpMessageHandler(
            _ => TooManyRequests());

        using var serviceProvider =
            CreateServiceProviderForPriceUpdater(handler);

        var updater =
            serviceProvider.GetRequiredService<IAmazonPriceUpdater>();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => updater.UpdatePriceAsync(
                sellerId: "LOCAL-SELLER-001",
                sku: "TEST-SKU-001",
                marketplaceId: "A33AVAJ2PDY3EV",
                productType: "PRODUCT",
                price: 99m,
                currencyCode: "TRY"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => updater.UpdatePriceAsync(
                sellerId: "LOCAL-SELLER-001",
                sku: "TEST-SKU-001",
                marketplaceId: "A33AVAJ2PDY3EV",
                productType: "PRODUCT",
                price: 99m,
                currencyCode: "TRY"));

        Assert.Equal(8, handler.RequestCount);

        var requestCountBeforeOpenCircuit =
            handler.RequestCount;

        await Assert.ThrowsAnyAsync<Exception>(
            () => updater.UpdatePriceAsync(
                sellerId: "LOCAL-SELLER-001",
                sku: "TEST-SKU-001",
                marketplaceId: "A33AVAJ2PDY3EV",
                productType: "PRODUCT",
                price: 99m,
                currencyCode: "TRY"));

        Assert.Equal(
            requestCountBeforeOpenCircuit,
            handler.RequestCount);
    }

    [Fact]
    public async Task PriceUpdater_DoesNotRetry500()
    {
        var handler = new CountingHttpMessageHandler(
            _ => JsonResponse(
                HttpStatusCode.InternalServerError,
                """
                {
                  "errors": [
                    {
                      "code": "InternalFailure",
                      "message": "Simulated server failure."
                    }
                  ]
                }
                """));

        using var serviceProvider =
            CreateServiceProviderForPriceUpdater(handler);

        var updater =
            serviceProvider.GetRequiredService<IAmazonPriceUpdater>();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => updater.UpdatePriceAsync(
                sellerId: "LOCAL-SELLER-001",
                sku: "TEST-SKU-001",
                marketplaceId: "A33AVAJ2PDY3EV",
                productType: "PRODUCT",
                price: 99m,
                currencyCode: "TRY"));

        Assert.Equal(1, handler.RequestCount);
    }

    private static ServiceProvider CreateServiceProviderForPricingProvider(
        HttpMessageHandler handler)
    {
        var services = CreateBaseServices();

        services.AddHttpClient<AmazonSpApiPricingProvider>()
            .ConfigurePrimaryHttpMessageHandler(
                () => handler);

        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateServiceProviderForSellers(
        HttpMessageHandler handler)
    {
        var services = CreateBaseServices();

        services.AddHttpClient<
                IAmazonSellersClient,
                AmazonSellersClient>()
            .ConfigurePrimaryHttpMessageHandler(
                () => handler);

        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateServiceProviderForPriceUpdater(
        HttpMessageHandler handler)
    {
        var services = CreateBaseServices();

        services.AddHttpClient<
                IAmazonPriceUpdater,
                AmazonListingsPriceUpdater>()
            .ConfigurePrimaryHttpMessageHandler(
                () => handler);

        return services.BuildServiceProvider();
    }

    private static ServiceCollection CreateBaseServices()
    {
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] =
                            "Host=localhost;Database=resilience_tests;" +
                            "Username=test;Password=test",

                        ["AmazonSpApi:Endpoint"] =
                            "https://sandbox.test",

                        ["AmazonSpApi:LwaEndpoint"] =
                            "https://lwa.test",

                        ["AmazonSpApi:MarketplaceId"] =
                            "A33AVAJ2PDY3EV",

                        ["AmazonSpApi:SellerId"] =
                            "LOCAL-SELLER-001"
                    })
                .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddInfrastructure(configuration);

        services.RemoveAll<ILwaAccessTokenProvider>();

        services.AddSingleton<ILwaAccessTokenProvider>(
            new FakeAccessTokenProvider());

        return services;
    }

    private static HttpResponseMessage TooManyRequests()
    {
        var response = JsonResponse(
            HttpStatusCode.TooManyRequests,
            """
            {
              "errors": [
                {
                  "code": "QuotaExceeded",
                  "message": "Rate limit exceeded."
                }
              ]
            }
            """);

        response.Headers.RetryAfter =
            new RetryConditionHeaderValue(
                TimeSpan.Zero);

        return response;
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json")
        };
    }

    private sealed class FakeAccessTokenProvider
        : ILwaAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                "resilience-test-access-token");
        }
    }

    private sealed class CountingHttpMessageHandler
        : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage>
            _responseFactory;

        private int _requestCount;

        public int RequestCount =>
            Volatile.Read(ref _requestCount);

        public CountingHttpMessageHandler(
            Func<int, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var attempt =
                Interlocked.Increment(ref _requestCount);

            return Task.FromResult(
                _responseFactory(attempt));
        }
    }
}
