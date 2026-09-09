using System.Net;
using System.Net.Http.Json;
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

        var originalConnectionString =
            Environment.GetEnvironmentVariable(
                connectionStringVariable);

        var originalIssuer =
            Environment.GetEnvironmentVariable(
                issuerVariable);

        var originalAudience =
            Environment.GetEnvironmentVariable(
                audienceVariable);

        var originalSigningKey =
            Environment.GetEnvironmentVariable(
                signingKeyVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                connectionStringVariable,
                _database.ConnectionString);

            Environment.SetEnvironmentVariable(
                issuerVariable,
                "AmazonRepricer.IntegrationTests");

            Environment.SetEnvironmentVariable(
                audienceVariable,
                "AmazonRepricer.IntegrationTests");

            Environment.SetEnvironmentVariable(
                signingKeyVariable,
                "integration-test-signing-key-32-bytes-minimum");

            using var factory =
                new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(
                        builder =>
                            builder.UseEnvironment("Testing"));

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
            Environment.SetEnvironmentVariable(
                connectionStringVariable,
                originalConnectionString);

            Environment.SetEnvironmentVariable(
                issuerVariable,
                originalIssuer);

            Environment.SetEnvironmentVariable(
                audienceVariable,
                originalAudience);

            Environment.SetEnvironmentVariable(
                signingKeyVariable,
                originalSigningKey);
        }
    }
}
