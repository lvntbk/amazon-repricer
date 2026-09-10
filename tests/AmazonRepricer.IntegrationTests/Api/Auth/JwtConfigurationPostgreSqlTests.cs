using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class JwtConfigurationPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public JwtConfigurationPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("1234567890123456789012345678901")]
    public async Task Startup_WithWeakSigningKey_FailsClosed(
        string signingKey)
    {
        var variables =
            new Dictionary<string, string?>
            {
                ["ConnectionStrings__DefaultConnection"] =
                    _database.ConnectionString,
                ["Jwt__Issuer"] =
                    "AmazonRepricer.IntegrationTests",
                ["Jwt__Audience"] =
                    "AmazonRepricer.IntegrationTests",
                ["Jwt__SigningKey"] =
                    signingKey,
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
                "signing key",
                exception.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
        }
    }
    [Fact]
    public async Task Startup_WithExactly32ByteSigningKey_Succeeds()
    {
        const string signingKey =
            "12345678901234567890123456789012";

        var variables =
            new Dictionary<string, string?>
            {
                ["ConnectionStrings__DefaultConnection"] =
                    _database.ConnectionString,
                ["Jwt__Issuer"] =
                    "AmazonRepricer.IntegrationTests",
                ["Jwt__Audience"] =
                    "AmazonRepricer.IntegrationTests",
                ["Jwt__SigningKey"] =
                    signingKey,
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

            var response =
                await client.GetAsync("/");

            Assert.NotEqual(
                System.Net.HttpStatusCode.InternalServerError,
                response.StatusCode);
        }
        finally
        {
        }
    }

    [Theory]
    [InlineData("Jwt__AccessTokenLifetimeMinutes", "0")]
    [InlineData("Jwt__AccessTokenLifetimeMinutes", "-1")]
    [InlineData("Jwt__AccessTokenLifetimeMinutes", "61")]
    [InlineData("Jwt__RefreshTokenLifetimeDays", "0")]
    [InlineData("Jwt__RefreshTokenLifetimeDays", "-1")]
    [InlineData("Jwt__RefreshTokenLifetimeDays", "91")]
    public async Task Startup_WithInvalidTokenLifetime_FailsClosed(
        string variableName,
        string variableValue)
    {
        var variables =
            new Dictionary<string, string?>
            {
                ["ConnectionStrings__DefaultConnection"] =
                    _database.ConnectionString,
                ["Jwt__Issuer"] =
                    "AmazonRepricer.IntegrationTests",
                ["Jwt__Audience"] =
                    "AmazonRepricer.IntegrationTests",
                ["Jwt__SigningKey"] =
                    "integration-test-signing-key-32-bytes-minimum",
                ["Jwt__AccessTokenLifetimeMinutes"] =
                    "15",
                ["Jwt__RefreshTokenLifetimeDays"] =
                    "30",
                ["AuthBootstrap__Enabled"] =
                    "false"
            };

        variables[variableName] =
            variableValue;

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
                "lifetime",
                exception.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
        }
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(60, 30)]
    [InlineData(15, 1)]
    [InlineData(15, 90)]
    public void Startup_WithBoundaryTokenLifetimes_Succeeds(
        int accessTokenLifetimeMinutes,
        int refreshTokenLifetimeDays)
    {
        var variables =
            new Dictionary<string, string?>
            {
                ["ConnectionStrings__DefaultConnection"] =
                    _database.ConnectionString,
                ["Jwt__Issuer"] =
                    "AmazonRepricer.IntegrationTests",
                ["Jwt__Audience"] =
                    "AmazonRepricer.IntegrationTests",
                ["Jwt__SigningKey"] =
                    "integration-test-signing-key-32-bytes-minimum",
                ["Jwt__AccessTokenLifetimeMinutes"] =
                    accessTokenLifetimeMinutes.ToString(),
                ["Jwt__RefreshTokenLifetimeDays"] =
                    refreshTokenLifetimeDays.ToString(),
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

            Assert.NotNull(client);
        }
        finally
        {
        }
    }

    [Theory]
    [InlineData("Jwt__Issuer", null)]
    [InlineData("Jwt__Issuer", "")]
    [InlineData("Jwt__Issuer", "   ")]
    [InlineData("Jwt__Audience", null)]
    [InlineData("Jwt__Audience", "")]
    [InlineData("Jwt__Audience", "   ")]
    public async Task Startup_WithMissingIssuerOrAudience_FailsClosed(
        string variableName,
        string? variableValue)
    {
        var variables =
            new Dictionary<string, string?>
            {
                ["ConnectionStrings__DefaultConnection"] =
                    _database.ConnectionString,
                ["Jwt__Issuer"] =
                    "AmazonRepricer.IntegrationTests",
                ["Jwt__Audience"] =
                    "AmazonRepricer.IntegrationTests",
                ["Jwt__SigningKey"] =
                    "integration-test-signing-key-32-bytes-minimum",
                ["Jwt__AccessTokenLifetimeMinutes"] =
                    "15",
                ["Jwt__RefreshTokenLifetimeDays"] =
                    "30",
                ["AuthBootstrap__Enabled"] =
                    "false"
            };

        variables[variableName] =
            variableValue;

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
                "JWT",
                exception.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
        }
    }

    [Fact]
    public async Task Startup_WithInvalidJwtConfiguration_DoesNotMutateAuthBootstrapState()
    {
        var variables =
            new Dictionary<string, string?>
            {
                ["ConnectionStrings__DefaultConnection"] =
                    _database.ConnectionString,
                ["Jwt__Issuer"] =
                    "",
                ["Jwt__Audience"] =
                    "AmazonRepricer.IntegrationTests",
                ["Jwt__SigningKey"] =
                    "integration-test-signing-key-32-bytes-minimum",
                ["Jwt__AccessTokenLifetimeMinutes"] =
                    "15",
                ["Jwt__RefreshTokenLifetimeDays"] =
                    "30",
                ["AuthBootstrap__Enabled"] =
                    "false"
            };

        using var environment =
            AuthTestEnvironmentScope.Capture(
                variables.Keys.ToArray());

        try
        {
            await using (var resetContext =
                _database.CreateAuthDbContext())
            {
                await resetContext.RefreshTokens
                    .ExecuteDeleteAsync();

                await resetContext.UserTokens
                    .ExecuteDeleteAsync();

                await resetContext.UserLogins
                    .ExecuteDeleteAsync();

                await resetContext.UserClaims
                    .ExecuteDeleteAsync();

                await resetContext.UserRoles
                    .ExecuteDeleteAsync();

                await resetContext.RoleClaims
                    .ExecuteDeleteAsync();

                await resetContext.Users
                    .ExecuteDeleteAsync();

                await resetContext.Roles
                    .ExecuteDeleteAsync();
            }

            foreach (var variable in variables)
            {
                environment.Set(variable.Key, variable.Value);
            }

            using var factory =
                AuthTestFactory.Create();

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

            await using var verificationContext =
                _database.CreateAuthDbContext();

            Assert.False(
                await verificationContext.Roles.AnyAsync());

            Assert.False(
                await verificationContext.Users.AnyAsync());
        }
        finally
        {
        }
    }

}
