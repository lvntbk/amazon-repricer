using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class LoginRateLimitPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public LoginRateLimitPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Login_AfterTenRequestsWithinWindow_ReturnsTooManyRequests()
    {
        const string connectionStringVariable =
            "ConnectionStrings__DefaultConnection";

        const string issuerVariable =
            "Jwt__Issuer";

        const string audienceVariable =
            "Jwt__Audience";

        const string signingKeyVariable =
            "Jwt__SigningKey";

        using var environment =
            AuthTestEnvironmentScope.Capture(
                connectionStringVariable,
                issuerVariable,
                audienceVariable,
                signingKeyVariable);

try
        {
            environment.Set(connectionStringVariable, _database.ConnectionString);

            environment.Set(issuerVariable, "AmazonRepricer.IntegrationTests");

            environment.Set(audienceVariable, "AmazonRepricer.IntegrationTests");

            environment.Set(signingKeyVariable, "integration-test-signing-key-32-bytes-minimum");

            using var factory =
                AuthTestFactory.Create();

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            for (var attempt = 1; attempt <= 10; attempt++)
            {
                var response =
                    await client.PostAsJsonAsync(
                        "/api/auth/login",
                        new
                        {
                            Email =
                                "rate-limit-unknown@example.test",
                            Password =
                                "Wrong-Horse-2026!"
                        });

                Assert.Equal(
                    HttpStatusCode.Unauthorized,
                    response.StatusCode);
            }

            var rateLimitedResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new
                    {
                        Email =
                            "rate-limit-unknown@example.test",
                        Password =
                            "Wrong-Horse-2026!"
                    });

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                rateLimitedResponse.StatusCode);

            Assert.True(
                rateLimitedResponse.Headers.TryGetValues(
                    "Retry-After",
                    out var retryAfterValues));

            var retryAfter =
                Assert.Single(retryAfterValues);

            Assert.True(
                int.TryParse(
                    retryAfter,
                    out var retryAfterSeconds));

            Assert.InRange(
                retryAfterSeconds,
                1,
                60);
        }
        finally
        {



        }
    }

    [Fact]
    public async Task Login_FromDifferentForwardedClientIpBehindTrustedProxy_UsesSeparateRateLimitPartition()
    {
        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString,
                ("ReverseProxy__Enabled", "true"),
                ("ReverseProxy__KnownProxies__0", "127.0.0.1"));

        using var factory =
            AuthTestFactory.Create();

        async Task<HttpStatusCode> SendLoginAsync(
            string forwardedFor)
        {
            var payload =
                JsonSerializer.Serialize(
                    new
                    {
                        Email =
                            "forwarded-rate-limit@example.test",
                        Password =
                            "Wrong-Horse-2026!"
                    });

            var payloadBytes =
                Encoding.UTF8.GetBytes(payload);

            using var body =
                new MemoryStream(payloadBytes);

            var context =
                await factory.Server.SendAsync(
                    httpContext =>
                    {
                        httpContext.Connection.RemoteIpAddress =
                            IPAddress.Loopback;

                        httpContext.Request.Scheme =
                            "https";

                        httpContext.Request.Method =
                            "POST";

                        httpContext.Request.Path =
                            "/api/auth/login";

                        httpContext.Request.Headers[
                            "X-Forwarded-For"] =
                            forwardedFor;

                        httpContext.Request.ContentType =
                            "application/json";

                        httpContext.Request.ContentLength =
                            payloadBytes.Length;

                        httpContext.Request.Body =
                            body;
                    });

            return (HttpStatusCode)
                context.Response.StatusCode;
        }

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var statusCode =
                await SendLoginAsync(
                    "198.51.100.10");

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                statusCode);
        }

        var differentClientStatusCode =
            await SendLoginAsync(
                "203.0.113.20");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            differentClientStatusCode);
    }


    [Fact]
    public async Task Login_FromUntrustedProxy_IgnoresForwardedClientIpForRateLimitPartition()
    {
        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString,
                ("ReverseProxy__Enabled", "true"),
                ("ReverseProxy__KnownProxies__0", "127.0.0.1"));

        using var factory =
            AuthTestFactory.Create();

        async Task<HttpStatusCode> SendLoginAsync(
            string forwardedFor)
        {
            var payload =
                JsonSerializer.Serialize(
                    new
                    {
                        Email =
                            "untrusted-forwarded@example.test",
                        Password =
                            "Wrong-Horse-2026!"
                    });

            var payloadBytes =
                Encoding.UTF8.GetBytes(payload);

            using var body =
                new MemoryStream(payloadBytes);

            var context =
                await factory.Server.SendAsync(
                    httpContext =>
                    {
                        httpContext.Connection.RemoteIpAddress =
                            IPAddress.Parse("10.20.30.40");

                        httpContext.Request.Scheme =
                            "https";

                        httpContext.Request.Method =
                            "POST";

                        httpContext.Request.Path =
                            "/api/auth/login";

                        httpContext.Request.Headers[
                            "X-Forwarded-For"] =
                            forwardedFor;

                        httpContext.Request.ContentType =
                            "application/json";

                        httpContext.Request.ContentLength =
                            payloadBytes.Length;

                        httpContext.Request.Body =
                            body;
                    });

            return (HttpStatusCode)
                context.Response.StatusCode;
        }

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var statusCode =
                await SendLoginAsync(
                    "198.51.100.10");

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                statusCode);
        }

        var spoofedClientStatusCode =
            await SendLoginAsync(
                "203.0.113.20");

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            spoofedClientStatusCode);
    }

}
