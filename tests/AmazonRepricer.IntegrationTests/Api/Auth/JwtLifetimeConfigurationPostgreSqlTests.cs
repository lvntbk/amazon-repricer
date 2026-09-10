using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class JwtLifetimeConfigurationPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public JwtLifetimeConfigurationPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Login_UsesConfiguredAccessTokenLifetime()
    {
        const string password =
            "Correct-Horse-2026!";

        var email =
            $"jwt-lifetime-{Guid.NewGuid():N}@example.test";

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
                    "5",
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

            using (var scope =
                factory.Services.CreateScope())
            {
                var userManager =
                    scope.ServiceProvider
                        .GetRequiredService<
                            UserManager<AppUser>>();

                var user =
                    new AppUser
                    {
                        Id = Guid.NewGuid(),
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true,
                        IsActive = true
                    };

                var createResult =
                    await userManager.CreateAsync(
                        user,
                        password);

                Assert.True(
                    createResult.Succeeded,
                    string.Join(
                        "; ",
                        createResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));

                var roleResult =
                    await userManager.AddToRoleAsync(
                        user,
                        AppRoles.Operator);

                Assert.True(
                    roleResult.Succeeded,
                    string.Join(
                        "; ",
                        roleResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));
            }

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            var beforeLogin =
                DateTimeOffset.UtcNow;

            var response =
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new
                    {
                        Email = email,
                        Password = password
                    });

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var payload =
                await response.Content
                    .ReadFromJsonAsync<LoginResponse>();

            Assert.NotNull(payload);

            Assert.InRange(
                payload.ExpiresAtUtc,
                beforeLogin.AddMinutes(4),
                beforeLogin.AddMinutes(6));
        }
        finally
        {
        }
    }

    private sealed record LoginResponse(
        string AccessToken,
        string TokenType,
        DateTimeOffset ExpiresAtUtc,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAtUtc);
    [Fact]
    public async Task Login_UsesConfiguredRefreshTokenLifetime()
    {
        const string password =
            "Correct-Horse-2026!";

        var email =
            $"refresh-lifetime-{Guid.NewGuid():N}@example.test";

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
                ["Jwt__RefreshTokenLifetimeDays"] =
                    "7",
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

            using (var scope =
                factory.Services.CreateScope())
            {
                var userManager =
                    scope.ServiceProvider
                        .GetRequiredService<
                            UserManager<AppUser>>();

                var user =
                    new AppUser
                    {
                        Id = Guid.NewGuid(),
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true,
                        IsActive = true
                    };

                var createResult =
                    await userManager.CreateAsync(
                        user,
                        password);

                Assert.True(
                    createResult.Succeeded,
                    string.Join(
                        "; ",
                        createResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));

                var roleResult =
                    await userManager.AddToRoleAsync(
                        user,
                        AppRoles.Operator);

                Assert.True(
                    roleResult.Succeeded,
                    string.Join(
                        "; ",
                        roleResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));
            }

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            var beforeLogin =
                DateTimeOffset.UtcNow;

            var response =
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new
                    {
                        Email = email,
                        Password = password
                    });

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var payload =
                await response.Content
                    .ReadFromJsonAsync<LoginResponse>();

            Assert.NotNull(payload);

            Assert.InRange(
                payload.RefreshTokenExpiresAtUtc,
                beforeLogin.AddDays(6),
                beforeLogin.AddDays(8));
        }
        finally
        {
        }
    }

    [Fact]
    public async Task Refresh_UsesConfiguredRefreshTokenLifetimeForReplacementToken()
    {
        const string password =
            "Correct-Horse-2026!";

        var email =
            $"refresh-rotation-lifetime-{Guid.NewGuid():N}@example.test";

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
                ["Jwt__RefreshTokenLifetimeDays"] =
                    "7",
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

            using (var scope =
                factory.Services.CreateScope())
            {
                var userManager =
                    scope.ServiceProvider
                        .GetRequiredService<
                            UserManager<AppUser>>();

                var user =
                    new AppUser
                    {
                        Id = Guid.NewGuid(),
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true,
                        IsActive = true
                    };

                var createResult =
                    await userManager.CreateAsync(
                        user,
                        password);

                Assert.True(
                    createResult.Succeeded,
                    string.Join(
                        "; ",
                        createResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));

                var roleResult =
                    await userManager.AddToRoleAsync(
                        user,
                        AppRoles.Operator);

                Assert.True(
                    roleResult.Succeeded,
                    string.Join(
                        "; ",
                        roleResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));
            }

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            var loginResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new
                    {
                        Email = email,
                        Password = password
                    });

            Assert.Equal(
                HttpStatusCode.OK,
                loginResponse.StatusCode);

            var loginPayload =
                await loginResponse.Content
                    .ReadFromJsonAsync<LoginResponse>();

            Assert.NotNull(loginPayload);

            var beforeRefresh =
                DateTimeOffset.UtcNow;

            var refreshResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new
                    {
                        RefreshToken =
                            loginPayload.RefreshToken
                    });

            Assert.Equal(
                HttpStatusCode.OK,
                refreshResponse.StatusCode);

            var refreshPayload =
                await refreshResponse.Content
                    .ReadFromJsonAsync<LoginResponse>();

            Assert.NotNull(refreshPayload);

            Assert.NotEqual(
                loginPayload.RefreshToken,
                refreshPayload.RefreshToken);

            Assert.InRange(
                refreshPayload.RefreshTokenExpiresAtUtc,
                beforeRefresh.AddDays(6),
                beforeRefresh.AddDays(8));
        }
        finally
        {
        }
    }

}
