using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using AmazonRepricer.IntegrationTests.PostgreSql;
using System.Net;

namespace AmazonRepricer.IntegrationTests.Api.Observability;

[Collection(PostgreSqlCollection.Name)]
public sealed class CorrelationIdPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public CorrelationIdPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task RequestWithoutCorrelationId_ReturnsGeneratedCorrelationId()
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

        Assert.True(
            response.Headers.TryGetValues(
                "X-Correlation-ID",
                out var values));

        var correlationId =
            Assert.Single(values);

        Assert.False(
            string.IsNullOrWhiteSpace(
                correlationId));
    }

    [Fact]
    public async Task RequestWithCorrelationId_PreservesCorrelationId()
    {
        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        using var factory =
            AuthTestFactory.Create();

        using var client =
            AuthTestClientFactory.Create(factory);

        const string correlationId =
            "integration-test-correlation-id";

        client.DefaultRequestHeaders.Add(
            "X-Correlation-ID",
            correlationId);

        var response =
            await client.GetAsync("/health/live");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        Assert.Equal(
            correlationId,
            Assert.Single(
                response.Headers.GetValues(
                    "X-Correlation-ID")));
    }

    [Fact]
    public async Task RequestWithInvalidCorrelationId_ReplacesCorrelationId()
    {
        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        using var factory =
            AuthTestFactory.Create();

        using var client =
            AuthTestClientFactory.Create(factory);

        const string invalidCorrelationId =
            "invalid correlation id";

        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Correlation-ID",
            invalidCorrelationId);

        var response =
            await client.GetAsync("/health/live");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var returnedCorrelationId =
            Assert.Single(
                response.Headers.GetValues(
                    "X-Correlation-ID"));

        Assert.NotEqual(
            invalidCorrelationId,
            returnedCorrelationId);

        Assert.False(
            string.IsNullOrWhiteSpace(
                returnedCorrelationId));
    }

}
