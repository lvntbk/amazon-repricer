using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Infrastructure.Amazon.Sellers;
using AmazonRepricer.Infrastructure.Persistence;
using AmazonRepricer.Infrastructure.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Microsoft.Extensions.Http.Resilience;

namespace AmazonRepricer.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' was not found.");

        services.AddDbContext<RepricerDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<
            IPriceUpdateSafetyGate,
            DbPriceUpdateSafetyGate>();

        services.Configure<AmazonSpApiOptions>(
            configuration.GetSection(AmazonSpApiOptions.SectionName));

        services.AddHttpClient(
            "AmazonLwa",
            ConfigureLwaClient);

        services.AddSingleton<ILwaAccessTokenProvider>(
            serviceProvider =>
            {
                var httpClientFactory =
                    serviceProvider.GetRequiredService<IHttpClientFactory>();

                var options =
                    serviceProvider.GetRequiredService<
                        IOptions<AmazonSpApiOptions>>();

                return new LwaAccessTokenProvider(
                    httpClientFactory.CreateClient("AmazonLwa"),
                    options);
            });

        services.AddHttpClient<AmazonSpApiPricingProvider>(
                ConfigureAmazonClient)
            .AddStandardResilienceHandler(
                ConfigureAmazonReadResilience);

        services.AddHttpClient<
                IAmazonSellersClient,
                AmazonSellersClient>(
                ConfigureAmazonClient)
            .AddStandardResilienceHandler(
                ConfigureAmazonReadResilience);

        // Price updates are state-changing PATCH requests.
        // Retry only explicit throttling responses. Transport failures,
        // timeouts and 5xx responses remain single-attempt because their
        // side-effect outcome may be uncertain.
        services.AddHttpClient<
                IAmazonPriceUpdater,
                AmazonListingsPriceUpdater>(
                ConfigureAmazonClient)
            .AddResilienceHandler(
                "AmazonWrite429",
                static builder =>
                {
                    builder.AddRetry(
                        new HttpRetryStrategyOptions
                        {
                            MaxRetryAttempts = 3,
                            Delay = TimeSpan.FromSeconds(2),
                            BackoffType =
                                Polly.DelayBackoffType.Exponential,
                            UseJitter = true,
                            ShouldRetryAfterHeader = true,
                            ShouldHandle = args =>
                                ValueTask.FromResult(
                                    args.Outcome.Result?.StatusCode ==
                                    System.Net.HttpStatusCode
                                        .TooManyRequests)
                        });

                    builder.AddCircuitBreaker(
                        new HttpCircuitBreakerStrategyOptions
                        {
                            FailureRatio = 0.5,
                            MinimumThroughput = 8,
                            SamplingDuration =
                                TimeSpan.FromSeconds(30),
                            BreakDuration =
                                TimeSpan.FromSeconds(30),
                            ShouldHandle = args =>
                                ValueTask.FromResult(
                                    args.Outcome.Result?.StatusCode ==
                                    System.Net.HttpStatusCode
                                        .TooManyRequests)
                        });
                });

        return services;
    }

    private static void ConfigureAmazonReadResilience(
        HttpStandardResilienceOptions options)
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromSeconds(2);
        options.Retry.BackoffType =
            Polly.DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.Retry.ShouldRetryAfterHeader = true;

        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.MinimumThroughput = 8;
        options.CircuitBreaker.SamplingDuration =
            TimeSpan.FromSeconds(30);
        options.CircuitBreaker.BreakDuration =
            TimeSpan.FromSeconds(30);
    }

    private static void ConfigureLwaClient(
        IServiceProvider serviceProvider,
        HttpClient client)
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<AmazonSpApiOptions>>()
            .Value;

        var endpoint = EnsureTrailingSlash(options.LwaEndpoint);

        client.BaseAddress = new Uri(endpoint);
        client.Timeout = TimeSpan.FromSeconds(30);
    }

    private static void ConfigureAmazonClient(
        IServiceProvider serviceProvider,
        HttpClient client)
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<AmazonSpApiOptions>>()
            .Value;

        var endpoint = EnsureTrailingSlash(options.Endpoint);

        client.BaseAddress = new Uri(endpoint);
        client.Timeout = TimeSpan.FromSeconds(30);
    }

    private static string EnsureTrailingSlash(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                "Amazon endpoint is required.");
        }

        return endpoint.EndsWith('/')
            ? endpoint
            : endpoint + "/";
    }
}
