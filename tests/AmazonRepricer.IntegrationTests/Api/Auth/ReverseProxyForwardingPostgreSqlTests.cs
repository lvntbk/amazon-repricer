using System.Net;
using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReverseProxyForwardingPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public ReverseProxyForwardingPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task TrustedProxy_WithForwardedHttps_DoesNotRedirect()
    {
        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString,
                ("ReverseProxy__Enabled", "true"),
                ("ReverseProxy__KnownProxies__0", "127.0.0.1"));

        using var factory =
            AuthTestFactory.Create(
                builder =>
                {
                    builder.ConfigureTestServices(
                        services =>
                        {
                            services.Configure<
                                HttpsRedirectionOptions>(
                                options =>
                                    options.HttpsPort = 443);
                        });
                });

        var context =
            await factory.Server.SendAsync(
                httpContext =>
                {
                    httpContext.Connection.RemoteIpAddress =
                        IPAddress.Loopback;

                    httpContext.Request.Scheme =
                        "http";

                    httpContext.Request.Method =
                        "GET";

                    httpContext.Request.Path =
                        "/health/live";

                    httpContext.Request.Headers[
                        "X-Forwarded-Proto"] =
                        "https";
                });

        Assert.Equal(
            (int)HttpStatusCode.OK,
            context.Response.StatusCode);
    }
}
