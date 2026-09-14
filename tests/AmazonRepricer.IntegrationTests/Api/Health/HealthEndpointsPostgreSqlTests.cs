using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using AmazonRepricer.IntegrationTests.PostgreSql;

namespace AmazonRepricer.IntegrationTests.Api.Health;

[Collection(PostgreSqlCollection.Name)]
public sealed class HealthEndpointsPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public HealthEndpointsPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Live_ReturnsOkWithoutAuthentication()
    {
        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        using var factory =
            AuthTestFactory.Create();

        using var client =
            AuthTestClientFactory.Create(factory);

        var response =
            await client.GetAsync("/health/live");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
    }

    [Fact]
    public async Task Ready_WithAvailablePostgreSql_ReturnsOkWithoutAuthentication()
    {
        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        using var factory =
            AuthTestFactory.Create();

        using var client =
            AuthTestClientFactory.Create(factory);

        var response =
            await client.GetAsync("/health/ready");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
    }

    [Fact]
    public async Task Ready_WhenPostgreSqlUnavailable_ReturnsServiceUnavailableWhileLiveRemainsOk()
    {
        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        using var factory =
            AuthTestFactory.Create(
                builder =>
                {
                    builder.ConfigureTestServices(
                        services =>
                        {
                            services.RemoveAll<
                                DbContextOptions<RepricerDbContext>>();

                            services.RemoveAll<
                                RepricerDbContext>();

                            services.AddDbContext<
                                RepricerDbContext>(
                                options =>
                                    options.UseNpgsql(
                                        "Host=127.0.0.1;Port=1;" +
                                        "Database=unavailable;" +
                                        "Username=unavailable;" +
                                        "Password=unavailable;" +
                                        "Timeout=1;Command Timeout=1"));
                        });
                });

        using var client =
            AuthTestClientFactory.Create(factory);

        var live =
            await client.GetAsync("/health/live");

        var ready =
            await client.GetAsync("/health/ready");

        Assert.Equal(
            HttpStatusCode.OK,
            live.StatusCode);

        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            ready.StatusCode);
    }
}
