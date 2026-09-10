using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using AmazonRepricer.Infrastructure.Identity;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class RefreshFailurePostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public RefreshFailurePostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Refresh_WithInactiveUser_ReturnsUnauthorizedWithoutIssuingNewToken()
    {
        const string connectionStringVariable =
            "ConnectionStrings__DefaultConnection";

        const string issuerVariable =
            "Jwt__Issuer";

        const string audienceVariable =
            "Jwt__Audience";

        const string signingKeyVariable =
            "Jwt__SigningKey";

        const string password =
            "Correct-Horse-2026!";

        var email =
            $"inactive-refresh-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

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
                        Id = userId,
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

            Guid originalRefreshTokenId;

            await using (var context =
                _database.CreateAuthDbContext())
            {
                var user =
                    await context.Users
                        .SingleAsync(
                            x => x.Id == userId);

                user.IsActive = false;

                await context.SaveChangesAsync();

                var originalRefreshToken =
                    await context.RefreshTokens
                        .SingleAsync(
                            x => x.UserId == userId);

                originalRefreshTokenId =
                    originalRefreshToken.Id;
            }

            var refreshResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new
                    {
                        RefreshToken =
                            loginPayload.RefreshToken
                    });

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                refreshResponse.StatusCode);

            await using var verificationContext =
                _database.CreateAuthDbContext();

            var refreshTokens =
                await verificationContext
                    .RefreshTokens
                    .Where(
                        x => x.UserId == userId)
                    .ToListAsync();

            Assert.Single(
                refreshTokens);

            Assert.Equal(
                originalRefreshTokenId,
                refreshTokens[0].Id);
        }
        finally
        {
            await using var cleanup =
                _database.CreateAuthDbContext();

            var persistedUser =
                await cleanup.Users
                    .FindAsync(userId);

            if (persistedUser is not null)
            {
                cleanup.Users.Remove(
                    persistedUser);

                await cleanup.SaveChangesAsync();
            }




        }
    }

    [Fact]
    public async Task Refresh_WithLockedUser_ReturnsUnauthorizedWithoutIssuingNewToken()
    {
        const string connectionStringVariable =
            "ConnectionStrings__DefaultConnection";

        const string issuerVariable =
            "Jwt__Issuer";

        const string audienceVariable =
            "Jwt__Audience";

        const string signingKeyVariable =
            "Jwt__SigningKey";

        const string password =
            "Correct-Horse-2026!";

        var email =
            $"locked-refresh-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

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
                        Id = userId,
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

            Guid originalRefreshTokenId;

            await using (var beforeLockContext =
                _database.CreateAuthDbContext())
            {
                var originalRefreshToken =
                    await beforeLockContext
                        .RefreshTokens
                        .SingleAsync(
                            x => x.UserId == userId);

                originalRefreshTokenId =
                    originalRefreshToken.Id;
            }

            using (var lockScope =
                factory.Services.CreateScope())
            {
                var userManager =
                    lockScope.ServiceProvider
                        .GetRequiredService<
                            UserManager<AppUser>>();

                var user =
                    await userManager.FindByIdAsync(
                        userId.ToString());

                Assert.NotNull(user);

                var lockResult =
                    await userManager.SetLockoutEndDateAsync(
                        user,
                        DateTimeOffset.UtcNow.AddMinutes(10));

                Assert.True(
                    lockResult.Succeeded,
                    string.Join(
                        "; ",
                        lockResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));

                Assert.True(
                    await userManager.IsLockedOutAsync(user));
            }

            var refreshResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new
                    {
                        RefreshToken =
                            loginPayload.RefreshToken
                    });

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                refreshResponse.StatusCode);

            await using var verificationContext =
                _database.CreateAuthDbContext();

            var refreshTokens =
                await verificationContext
                    .RefreshTokens
                    .Where(
                        x => x.UserId == userId)
                    .ToListAsync();

            Assert.Single(
                refreshTokens);

            Assert.Equal(
                originalRefreshTokenId,
                refreshTokens[0].Id);
        }
        finally
        {
            await using var cleanup =
                _database.CreateAuthDbContext();

            var persistedUser =
                await cleanup.Users
                    .FindAsync(userId);

            if (persistedUser is not null)
            {
                cleanup.Users.Remove(
                    persistedUser);

                await cleanup.SaveChangesAsync();
            }




        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-refresh-token")]
    [InlineData(
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Refresh_WithInvalidOrUnknownToken_ReturnsUnauthorized(
        string refreshToken)
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

            int tokenCountBefore;

            await using (var beforeContext =
                _database.CreateAuthDbContext())
            {
                tokenCountBefore =
                    await beforeContext.RefreshTokens.CountAsync();
            }

            var response =
                await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new
                    {
                        RefreshToken = refreshToken
                    });

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);

            await using var afterContext =
                _database.CreateAuthDbContext();

            var tokenCountAfter =
                await afterContext.RefreshTokens.CountAsync();

            Assert.Equal(
                tokenCountBefore,
                tokenCountAfter);
        }
        finally
        {



        }
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_ReturnsUnauthorizedWithoutIssuingNewToken()
    {
        const string connectionStringVariable =
            "ConnectionStrings__DefaultConnection";

        const string issuerVariable =
            "Jwt__Issuer";

        const string audienceVariable =
            "Jwt__Audience";

        const string signingKeyVariable =
            "Jwt__SigningKey";

        const string password =
            "Correct-Horse-2026!";

        var email =
            $"expired-refresh-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

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
                        Id = userId,
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

            Guid originalRefreshTokenId;

            await using (var expirationContext =
                _database.CreateAuthDbContext())
            {
                var refreshToken =
                    await expirationContext
                        .RefreshTokens
                        .SingleAsync(
                            x => x.UserId == userId);

                originalRefreshTokenId =
                    refreshToken.Id;

                refreshToken.CreatedAtUtc =
                    DateTime.UtcNow.AddDays(-2);

                refreshToken.ExpiresAtUtc =
                    DateTime.UtcNow.AddDays(-1);

                await expirationContext.SaveChangesAsync();
            }

            var refreshResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new
                    {
                        RefreshToken =
                            loginPayload.RefreshToken
                    });

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                refreshResponse.StatusCode);

            await using var verificationContext =
                _database.CreateAuthDbContext();

            var refreshTokens =
                await verificationContext
                    .RefreshTokens
                    .Where(
                        x => x.UserId == userId)
                    .ToListAsync();

            Assert.Single(
                refreshTokens);

            Assert.Equal(
                originalRefreshTokenId,
                refreshTokens[0].Id);

            Assert.True(
                refreshTokens[0].ExpiresAtUtc <
                DateTime.UtcNow);

            Assert.Null(
                refreshTokens[0].ReplacedByTokenId);
        }
        finally
        {
            await using var cleanup =
                _database.CreateAuthDbContext();

            var persistedUser =
                await cleanup.Users
                    .FindAsync(userId);

            if (persistedUser is not null)
            {
                cleanup.Users.Remove(
                    persistedUser);

                await cleanup.SaveChangesAsync();
            }




        }
    }

    private sealed record LoginResponse(
        string AccessToken,
        string TokenType,
        DateTimeOffset ExpiresAtUtc,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAtUtc);
}
