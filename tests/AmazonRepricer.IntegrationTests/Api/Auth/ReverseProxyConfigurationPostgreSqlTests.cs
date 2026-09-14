using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReverseProxyConfigurationPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public ReverseProxyConfigurationPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Startup_WhenReverseProxyEnabledWithoutTrustedProxy_FailsClosed()
    {
        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString,
                ("ReverseProxy__Enabled", "true"),
                ("ReverseProxy__KnownProxies__0", null));

        using var factory =
            AuthTestFactory.Create();

        var exception =
            await Assert.ThrowsAnyAsync<Exception>(
                async () =>
                {
                    using var client =
                        factory.CreateClient(
                            new WebApplicationFactoryClientOptions
                            {
                                AllowAutoRedirect = false
                            });

                    await client.GetAsync("/");
                });

        Assert.Contains(
            "trusted proxy",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Startup_WhenTrustedProxyIsInvalidIp_FailsClosed()
    {
        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString,
                ("ReverseProxy__Enabled", "true"),
                ("ReverseProxy__KnownProxies__0", "not-an-ip"));

        using var factory =
            AuthTestFactory.Create();

        var exception =
            await Assert.ThrowsAnyAsync<Exception>(
                async () =>
                {
                    using var client =
                        factory.CreateClient(
                            new WebApplicationFactoryClientOptions
                            {
                                AllowAutoRedirect = false
                            });

                    await client.GetAsync("/");
                });

        Assert.Contains(
            "valid IP",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }
}
