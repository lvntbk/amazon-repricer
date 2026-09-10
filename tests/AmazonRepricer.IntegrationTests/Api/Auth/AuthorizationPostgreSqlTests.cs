using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class AuthorizationPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public AuthorizationPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task CreateProduct_WithUnauthorizedRole_ReturnsForbidden()
    {
        const string connectionStringVariable =
            "ConnectionStrings__DefaultConnection";

        const string issuerVariable =
            "Jwt__Issuer";

        const string audienceVariable =
            "Jwt__Audience";

        const string signingKeyVariable =
            "Jwt__SigningKey";

        const string issuer =
            "AmazonRepricer.IntegrationTests";

        const string audience =
            "AmazonRepricer.IntegrationTests";

        const string signingKey =
            "integration-test-signing-key-32-bytes-minimum";

        using var environment =
            AuthTestEnvironmentScope.Capture(
                connectionStringVariable,
                issuerVariable,
                audienceVariable,
                signingKeyVariable);

try
        {
            environment.Set(connectionStringVariable, _database.ConnectionString);

            environment.Set(issuerVariable,
                issuer);

            environment.Set(audienceVariable,
                audience);

            environment.Set(signingKeyVariable,
                signingKey);

            using var factory =
                AuthTestFactory.Create();

            using var client = factory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false
                });

            var token = CreateToken(
                issuer,
                audience,
                signingKey,
                role: "Viewer");

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);

            var response =
                await client.PostAsJsonAsync(
                    "/api/products",
                    new
                    {
                        AmazonStoreId = Guid.NewGuid(),
                        Sku = "AUTH-ROLE-TEST",
                        Asin = "B0AUTHROLE",
                        Title = "Authorization role test",
                        Cost = 50m,
                        CurrentPrice = 100m
                    });

            Assert.Equal(
                HttpStatusCode.Forbidden,
                response.StatusCode);
        }
        finally
        {



        }
    }


    [Fact]
    public async Task CreateProduct_WithOperatorRole_ReturnsCreated()
    {
        const string connectionStringVariable =
            "ConnectionStrings__DefaultConnection";

        const string issuerVariable =
            "Jwt__Issuer";

        const string audienceVariable =
            "Jwt__Audience";

        const string signingKeyVariable =
            "Jwt__SigningKey";

        const string issuer =
            "AmazonRepricer.IntegrationTests";

        const string audience =
            "AmazonRepricer.IntegrationTests";

        const string signingKey =
            "integration-test-signing-key-32-bytes-minimum";

        var store = new AmazonStore
        {
            Name = "Authorization operator test store",
            SellerId = $"AUTH-OP-{Guid.NewGuid():N}",
            MarketplaceId = "AUTH-MARKETPLACE"
        };

        await using (var dbContext =
            _database.CreateDbContext())
        {
            dbContext.AmazonStores.Add(store);
            await dbContext.SaveChangesAsync();
        }

        using var environment =
            AuthTestEnvironmentScope.Capture(
                connectionStringVariable,
                issuerVariable,
                audienceVariable,
                signingKeyVariable);

try
        {
            environment.Set(connectionStringVariable, _database.ConnectionString);

            environment.Set(issuerVariable,
                issuer);

            environment.Set(audienceVariable,
                audience);

            environment.Set(signingKeyVariable,
                signingKey);

            using var factory =
                AuthTestFactory.Create();

            using var client = factory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false
                });

            var token = CreateToken(
                issuer,
                audience,
                signingKey,
                AppRoles.Operator);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);

            var response =
                await client.PostAsJsonAsync(
                    "/api/products",
                    new
                    {
                        AmazonStoreId = store.Id,
                        Sku = $"AUTH-OP-{Guid.NewGuid():N}",
                        Asin = "B0AUTHOP01",
                        Title = "Operator authorization test",
                        Cost = 50m,
                        CurrentPrice = 100m
                    });

            Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode);
        }
        finally
        {




            await using var cleanup =
                _database.CreateDbContext();

            var seededStore =
                await cleanup.AmazonStores.FindAsync(store.Id);

            if (seededStore is not null)
            {
                cleanup.AmazonStores.Remove(seededStore);
                await cleanup.SaveChangesAsync();
            }
        }
    }


    [Fact]
    public async Task Products_WithInvalidSignature_ReturnsUnauthorized()
    {
        const string connectionStringVariable =
            "ConnectionStrings__DefaultConnection";

        const string issuerVariable =
            "Jwt__Issuer";

        const string audienceVariable =
            "Jwt__Audience";

        const string signingKeyVariable =
            "Jwt__SigningKey";

        const string issuer =
            "AmazonRepricer.IntegrationTests";

        const string audience =
            "AmazonRepricer.IntegrationTests";

        const string validSigningKey =
            "integration-test-signing-key-32-bytes-minimum";

        const string invalidSigningKey =
            "different-integration-signing-key-32-bytes";

        using var environment =
            AuthTestEnvironmentScope.Capture(
                connectionStringVariable,
                issuerVariable,
                audienceVariable,
                signingKeyVariable);

try
        {
            environment.Set(connectionStringVariable, _database.ConnectionString);

            environment.Set(issuerVariable,
                issuer);

            environment.Set(audienceVariable,
                audience);

            environment.Set(signingKeyVariable,
                validSigningKey);

            using var factory =
                AuthTestFactory.Create();

            using var client = factory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false
                });

            var token = CreateToken(
                issuer,
                audience,
                invalidSigningKey,
                AppRoles.Operator);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);

            var response =
                await client.GetAsync("/api/products");

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }
        finally
        {



        }
    }


    [Fact]
    public async Task Products_WithExpiredToken_ReturnsUnauthorized()
    {
        const string connectionStringVariable =
            "ConnectionStrings__DefaultConnection";

        const string issuerVariable =
            "Jwt__Issuer";

        const string audienceVariable =
            "Jwt__Audience";

        const string signingKeyVariable =
            "Jwt__SigningKey";

        const string issuer =
            "AmazonRepricer.IntegrationTests";

        const string audience =
            "AmazonRepricer.IntegrationTests";

        const string signingKey =
            "integration-test-signing-key-32-bytes-minimum";

        using var environment =
            AuthTestEnvironmentScope.Capture(
                connectionStringVariable,
                issuerVariable,
                audienceVariable,
                signingKeyVariable);

try
        {
            environment.Set(connectionStringVariable, _database.ConnectionString);

            environment.Set(issuerVariable,
                issuer);

            environment.Set(audienceVariable,
                audience);

            environment.Set(signingKeyVariable,
                signingKey);

            using var factory =
                AuthTestFactory.Create();

            using var client = factory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false
                });

            var token = CreateExpiredToken(
                issuer,
                audience,
                signingKey,
                AppRoles.Operator);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);

            var response =
                await client.GetAsync("/api/products");

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }
        finally
        {



        }
    }


    [Fact]
    public async Task AmazonStores_WithoutAuthentication_ReturnsUnauthorized()
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

            using var client = factory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false
                });

            var response =
                await client.GetAsync("/api/amazon-stores");

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }
        finally
        {



        }
    }
    [Fact]
    public async Task Products_WithoutAuthentication_ReturnsUnauthorized()
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

            using var client = factory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false
                });

            var response =
                await client.GetAsync("/api/products");

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }
        finally
        {



        }
    }
    [Theory]
    [InlineData(
        "/api/amazon-stores",
        "{\\\"name\\\":\\\"\\\",\\\"sellerId\\\":\\\"AUTH\\\",\\\"marketplaceId\\\":\\\"AUTH\\\"}")]
    [InlineData(
        "/api/pricing-rules",
        "{\\\"productId\\\":\\\"00000000-0000-0000-0000-000000000001\\\",\\\"strategy\\\":0,\\\"minimumPrice\\\":10,\\\"maximumPrice\\\":20,\\\"adjustmentValue\\\":1,\\\"minimumProfitPercentage\\\":1}")]
    [InlineData(
        "/api/repricing/evaluate",
        "{\\\"productId\\\":\\\"00000000-0000-0000-0000-000000000001\\\",\\\"featuredOfferPrice\\\":10,\\\"isFeaturedOfferOurs\\\":false}")]
    [InlineData(
        "/api/repricing-events/00000000-0000-0000-0000-000000000001/approve",
        "{}")]
    [InlineData(
        "/api/repricing-events/00000000-0000-0000-0000-000000000001/reject",
        "{}")]
    [InlineData(
        "/api/repricing-events/00000000-0000-0000-0000-000000000001/apply",
        "{}")]
    public async Task MutationEndpoint_WithViewerRole_ReturnsForbidden(
        string requestUri,
        string json)
    {
        const string connectionStringVariable =
            "ConnectionStrings__DefaultConnection";

        const string issuerVariable =
            "Jwt__Issuer";

        const string audienceVariable =
            "Jwt__Audience";

        const string signingKeyVariable =
            "Jwt__SigningKey";

        const string issuer =
            "AmazonRepricer.IntegrationTests";

        const string audience =
            "AmazonRepricer.IntegrationTests";

        const string signingKey =
            "integration-test-signing-key-32-bytes-minimum";

        using var environment =
            AuthTestEnvironmentScope.Capture(
                connectionStringVariable,
                issuerVariable,
                audienceVariable,
                signingKeyVariable);

try
        {
            environment.Set(connectionStringVariable, _database.ConnectionString);

            environment.Set(issuerVariable,
                issuer);

            environment.Set(audienceVariable,
                audience);

            environment.Set(signingKeyVariable,
                signingKey);

            using var factory =
                AuthTestFactory.Create();

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            var token =
                CreateToken(
                    issuer,
                    audience,
                    signingKey,
                    role: "Viewer");

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    requestUri)
                {
                    Content =
                        new StringContent(
                            json,
                            Encoding.UTF8,
                            "application/json")
                };

            var response =
                await client.SendAsync(
                    request);

            Assert.Equal(
                HttpStatusCode.Forbidden,
                response.StatusCode);
        }
        finally
        {



        }
    }

    private static string CreateExpiredToken(
        string issuer,
        string audience,
        string signingKey,
        string role)
    {
        var key =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(signingKey));

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
                notBefore: DateTime.UtcNow.AddMinutes(-10),
                expires: DateTime.UtcNow.AddMinutes(-5),
                signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }


    private static string CreateToken(
        string issuer,
        string audience,
        string signingKey,
        string role)
    {
        var key =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(signingKey));

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
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }

}
