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

        services.AddHttpClient<
            ILwaAccessTokenProvider,
            LwaAccessTokenProvider>(
            client =>
            {
                client.BaseAddress =
                    new Uri("https://api.amazon.com/");

                client.Timeout = TimeSpan.FromSeconds(30);
            });

        services.AddHttpClient<AmazonSpApiPricingProvider>(
            ConfigureAmazonClient);

        services.AddHttpClient<
            IAmazonSellersClient,
            AmazonSellersClient>(
            ConfigureAmazonClient);

        return services;
    }

    private static void ConfigureAmazonClient(
        IServiceProvider serviceProvider,
        HttpClient client)
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<AmazonSpApiOptions>>()
            .Value;

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException(
                "AmazonSpApi:Endpoint is required.");
        }

        var endpoint = options.Endpoint.EndsWith('/')
            ? options.Endpoint
            : options.Endpoint + "/";

        client.BaseAddress = new Uri(endpoint);
        client.Timeout = TimeSpan.FromSeconds(30);
    }
}
