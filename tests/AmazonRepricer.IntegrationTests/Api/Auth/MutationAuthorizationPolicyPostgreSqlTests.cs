using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Security.Claims;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class MutationAuthorizationPolicyPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public MutationAuthorizationPolicyPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Theory]
    [InlineData(
        AppRoles.Operator,
        "/api/amazon-stores",
        "{\"name\":\"\",\"sellerId\":\"AUTH\",\"marketplaceId\":\"AUTH\"}")]
    [InlineData(
        AppRoles.Admin,
        "/api/amazon-stores",
        "{\"name\":\"\",\"sellerId\":\"AUTH\",\"marketplaceId\":\"AUTH\"}")]
    [InlineData(
        AppRoles.Operator,
        "/api/pricing-rules",
        "{\"productId\":\"00000000-0000-0000-0000-000000000001\",\"strategy\":0,\"minimumPrice\":10,\"maximumPrice\":20,\"adjustmentValue\":1,\"minimumProfitPercentage\":1}")]
    [InlineData(
        AppRoles.Admin,
        "/api/pricing-rules",
        "{\"productId\":\"00000000-0000-0000-0000-000000000001\",\"strategy\":0,\"minimumPrice\":10,\"maximumPrice\":20,\"adjustmentValue\":1,\"minimumProfitPercentage\":1}")]
    [InlineData(
        AppRoles.Operator,
        "/api/repricing/evaluate",
        "{\"productId\":\"00000000-0000-0000-0000-000000000001\",\"featuredOfferPrice\":10,\"isFeaturedOfferOurs\":false}")]
    [InlineData(
        AppRoles.Admin,
        "/api/repricing/evaluate",
        "{\"productId\":\"00000000-0000-0000-0000-000000000001\",\"featuredOfferPrice\":10,\"isFeaturedOfferOurs\":false}")]
    [InlineData(
        AppRoles.Operator,
        "/api/repricing-events/00000000-0000-0000-0000-000000000001/approve",
        "{}")]
    [InlineData(
        AppRoles.Admin,
        "/api/repricing-events/00000000-0000-0000-0000-000000000001/approve",
        "{}")]
    [InlineData(
        AppRoles.Operator,
        "/api/repricing-events/00000000-0000-0000-0000-000000000001/reject",
        "{}")]
    [InlineData(
        AppRoles.Admin,
        "/api/repricing-events/00000000-0000-0000-0000-000000000001/reject",
        "{}")]
    [InlineData(
        AppRoles.Operator,
        "/api/repricing-events/00000000-0000-0000-0000-000000000001/apply",
        "{}")]
    [InlineData(
        AppRoles.Admin,
        "/api/repricing-events/00000000-0000-0000-0000-000000000001/apply",
        "{}")]
    public async Task MutationEndpoint_WithAuthorizedRole_PassesAuthorization(
        string role,
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
                    role);

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

            Assert.NotEqual(
                HttpStatusCode.Unauthorized,
                response.StatusCode);

            Assert.NotEqual(
                HttpStatusCode.Forbidden,
                response.StatusCode);
        }
        finally
        {



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
}
