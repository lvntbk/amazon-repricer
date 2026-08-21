using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Infrastructure.Amazon.Sellers;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
            ConfigureAmazonClient);

        services.AddHttpClient<
            IAmazonSellersClient,
            AmazonSellersClient>(
            ConfigureAmazonClient);

        return services;
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
