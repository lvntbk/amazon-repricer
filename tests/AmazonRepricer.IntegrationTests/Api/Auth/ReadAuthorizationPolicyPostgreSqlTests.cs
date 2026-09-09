using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using AmazonRepricer.Infrastructure.Amazon.Sellers;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReadAuthorizationPolicyPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public ReadAuthorizationPolicyPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task ReadEndpoints_WithoutAuthentication_ReturnUnauthorized()
    {
        await WithFactoryAsync(
            async factory =>
            {
                using var client =
                    factory.CreateClient(
                        new WebApplicationFactoryClientOptions
                        {
                            AllowAutoRedirect = false
                        });

                var requestUris = new[]
                {
                    "/api/amazon-stores",
                    "/api/amazon-stores/00000000-0000-0000-0000-000000000001",
                    "/api/pricing-rules/00000000-0000-0000-0000-000000000001",
                    "/api/products",
                    "/api/products/00000000-0000-0000-0000-000000000001",
                    "/api/repricing-events",
                    "/api/repricing-events/00000000-0000-0000-0000-000000000001",
                    "/api/amazon/pricing?asin=&sku=",
                    "/api/amazon/connection-test"
                };

                foreach (var requestUri in requestUris)
                {
                    var response =
                        await client.GetAsync(
                            requestUri);

                    Assert.Equal(
                        HttpStatusCode.Unauthorized,
                        response.StatusCode);
                }
            });
    }

    [Fact]
    public async Task ReadEndpoints_WithViewerRole_PassAuthorization()
    {
        await WithFactoryAsync(
            async factory =>
            {
                using var client =
                    factory.CreateClient(
                        new WebApplicationFactoryClientOptions
                        {
                            AllowAutoRedirect = false
                        });

                var token =
                    CreateToken(
                        "AmazonRepricer.IntegrationTests",
                        "AmazonRepricer.IntegrationTests",
                        "integration-test-signing-key-32-bytes-minimum",
                        "Viewer");

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        token);

                var requestUris = new[]
                {
                    "/api/amazon-stores",
                    "/api/amazon-stores/00000000-0000-0000-0000-000000000001",
                    "/api/pricing-rules/00000000-0000-0000-0000-000000000001",
                    "/api/products",
                    "/api/products/00000000-0000-0000-0000-000000000001",
                    "/api/repricing-events",
                    "/api/repricing-events/00000000-0000-0000-0000-000000000001",
                    "/api/amazon/pricing?asin=&sku=",
                    "/api/amazon/connection-test"
                };

                foreach (var requestUri in requestUris)
                {
                    var response =
                        await client.GetAsync(
                            requestUri);

                    Assert.NotEqual(
                        HttpStatusCode.Unauthorized,
                        response.StatusCode);

                    Assert.NotEqual(
                        HttpStatusCode.Forbidden,
                        response.StatusCode);
                }
            });
    }

    private async Task WithFactoryAsync(
        Func<WebApplicationFactory<Program>, Task> test)
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
                        {
                            builder.UseEnvironment("Testing");

                            builder.ConfigureTestServices(
                                services =>
                                {
                                    services.RemoveAll<
                                        IAmazonSellersClient>();

                                    services.AddSingleton<
                                        IAmazonSellersClient,
                                        FakeAmazonSellersClient>();
                                });
                        });

            await test(factory);
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

    private static string CreateToken(
        string issuer,
        string audience,
        string signingKey,
        string role)
    {
        var key =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    signingKey));

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

        var token =
            new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims:
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        Guid.NewGuid().ToString()),
                    new Claim(
                        ClaimTypes.Role,
                        role)
                ],
                notBefore:
                    DateTime.UtcNow.AddMinutes(-1),
                expires:
                    DateTime.UtcNow.AddMinutes(10),
                signingCredentials:
                    credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }

    private sealed class FakeAmazonSellersClient
        : IAmazonSellersClient
    {
        public Task<
            IReadOnlyList<AmazonMarketplaceParticipation>>
            GetMarketplaceParticipationsAsync(
                CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AmazonMarketplaceParticipation> result =
                Array.Empty<AmazonMarketplaceParticipation>();

            return Task.FromResult(result);
        }
    }
}
