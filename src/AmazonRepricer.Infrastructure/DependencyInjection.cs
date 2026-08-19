using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AmazonRepricer.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' was not found.");

        services.AddDbContext<RepricerDbContext>(options =>
            options.UseNpgsql(connectionString));

services.AddHttpClient<ILwaAccessTokenProvider, LwaAccessTokenProvider>(
            client =>
            {
                client.BaseAddress = new Uri("https://api.amazon.com/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

        return services;
    }
}
