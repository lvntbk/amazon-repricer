using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AmazonRepricer.Api.Auth;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class AccessTokenServicePostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public AccessTokenServicePostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public void AccessTokenService_CreatesTokenWithConfiguredIdentityAndClaims()
    {
        var variables =
            new Dictionary<string, string?>
            {
                ["ConnectionStrings__DefaultConnection"] =
                    _database.ConnectionString,
                ["Jwt__Issuer"] =
                    "AmazonRepricer.IntegrationTests",
                ["Jwt__Audience"] =
                    "AmazonRepricer.IntegrationTests.Audience",
                ["Jwt__SigningKey"] =
                    "integration-test-signing-key-32-bytes-minimum",
                ["AuthBootstrap__Enabled"] =
                    "false"
            };

        using var environment =
            AuthTestEnvironmentScope.Capture(
                variables.Keys.ToArray());

        try
        {
            foreach (var variable in variables)
            {
                environment.Set(variable.Key, variable.Value);
            }

            using var factory =
                AuthTestFactory.Create();

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            using var scope =
                factory.Services.CreateScope();

            var service =
                scope.ServiceProvider
                    .GetRequiredService<IAccessTokenService>();

            var user =
                new AppUser
                {
                    Id = Guid.NewGuid(),
                    Email = "token-user@example.test",
                    UserName = "token-user@example.test",
                    IsActive = true
                };

            var issuedAtUtc =
                new DateTimeOffset(
                    2026,
                    9,
                    10,
                    12,
                    0,
                    0,
                    TimeSpan.Zero);

            var expiresAtUtc =
                issuedAtUtc.AddMinutes(15);

            var encodedToken =
                service.CreateAccessToken(
                    user,
                    new[]
                    {
                        AppRoles.Admin,
                        AppRoles.Operator
                    },
                    issuedAtUtc,
                    expiresAtUtc);

            var token =
                new JwtSecurityTokenHandler()
                    .ReadJwtToken(encodedToken);

            Assert.Equal(
                "AmazonRepricer.IntegrationTests",
                token.Issuer);

            Assert.Contains(
                "AmazonRepricer.IntegrationTests.Audience",
                token.Audiences);

            Assert.Contains(
                token.Claims,
                claim =>
                    claim.Type == ClaimTypes.NameIdentifier &&
                    claim.Value == user.Id.ToString());

            Assert.Contains(
                token.Claims,
                claim =>
                    claim.Type == ClaimTypes.Email &&
                    claim.Value == user.Email);

            Assert.Contains(
                token.Claims,
                claim =>
                    claim.Type == ClaimTypes.Role &&
                    claim.Value == AppRoles.Admin);

            Assert.Contains(
                token.Claims,
                claim =>
                    claim.Type == ClaimTypes.Role &&
                    claim.Value == AppRoles.Operator);

            Assert.Contains(
                token.Claims,
                claim =>
                    claim.Type == JwtRegisteredClaimNames.Jti &&
                    !string.IsNullOrWhiteSpace(claim.Value));

            Assert.Equal(
                issuedAtUtc.UtcDateTime,
                token.ValidFrom);

            Assert.Equal(
                expiresAtUtc.UtcDateTime,
                token.ValidTo);
        }
        finally
        {
        }
    }
}
