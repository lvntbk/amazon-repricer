using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class RefreshRateLimitPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public RefreshRateLimitPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Refresh_AfterTwentyRequestsWithinWindow_ReturnsTooManyRequests()
    {
        const string connectionStringVariable =
            "ConnectionStrings__DefaultConnection";

        const string issuerVariable =
            "Jwt__Issuer";

        const string audienceVariable =
            "Jwt__Audience";

        const string signingKeyVariable =
            "Jwt__SigningKey";

        const string unknownRefreshToken =
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

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

            for (var attempt = 1; attempt <= 20; attempt++)
            {
                var response =
                    await client.PostAsJsonAsync(
                        "/api/auth/refresh",
                        new
                        {
                            RefreshToken =
                                unknownRefreshToken
                        });

                Assert.Equal(
                    HttpStatusCode.Unauthorized,
                    response.StatusCode);
            }

            var rateLimitedResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new
                    {
                        RefreshToken =
                            unknownRefreshToken
                    });

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                rateLimitedResponse.StatusCode);

            Assert.True(
                rateLimitedResponse.Headers.TryGetValues(
                    "Retry-After",
                    out var retryAfterValues));

            var retryAfter =
                Assert.Single(
                    retryAfterValues);

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
}
