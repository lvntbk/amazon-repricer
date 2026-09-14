using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class AccessTokenRevocationPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public AccessTokenRevocationPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task RoleChange_InvalidatesPreviouslyIssuedAccessToken()
    {
        const string password =
            "Security-State-2026!";

        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var actorEmail =
            $"security-actor-{Guid.NewGuid():N}@example.test";

        var targetEmail =
            $"security-target-{Guid.NewGuid():N}@example.test";

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        try
        {
            using var factory =
                AuthTestFactory
                    .CreateWithProductionTokenStateValidation();

            using (var scope =
                factory.Services.CreateScope())
            {
                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    actorEmail,
                    password,
                    AppRoles.Admin,
                    isActive: true,
                    userId: actorId);

                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    targetEmail,
                    password,
                    AppRoles.Admin,
                    isActive: true,
                    userId: targetId);
            }

            using var actorClient =
                AuthTestClientFactory.Create(factory);

            using var targetClient =
                AuthTestClientFactory.Create(factory);

            async Task<string> LoginAsync(
                HttpClient client,
                string email)
            {
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

                var json =
                    await response.Content
                        .ReadFromJsonAsync<JsonElement>();

                return json
                    .GetProperty("accessToken")
                    .GetString()!;
            }

            actorClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    await LoginAsync(
                        actorClient,
                        actorEmail));

            targetClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    await LoginAsync(
                        targetClient,
                        targetEmail));

            var roleChangeResponse =
                await actorClient.PutAsJsonAsync(
                    $"/api/admin/users/{targetId}/role",
                    new
                    {
                        Role = AppRoles.Operator
                    });

            Assert.Equal(
                HttpStatusCode.NoContent,
                roleChangeResponse.StatusCode);

            var staleTokenResponse =
                await targetClient.PostAsJsonAsync(
                    "/api/admin/users",
                    new { });

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                staleTokenResponse.StatusCode);
        }
        finally
        {
            await using var cleanup =
                _database.CreateAuthDbContext();

            var userIds =
                new[] { actorId, targetId };

            await cleanup.RefreshTokens
                .Where(x => userIds.Contains(x.UserId))
                .ExecuteDeleteAsync();

            await cleanup.Users
                .Where(x => userIds.Contains(x.Id))
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Deactivation_InvalidatesPreviouslyIssuedAccessToken()
    {
        const string password =
            "Security-State-2026!";

        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var actorEmail =
            $"deactivate-actor-{Guid.NewGuid():N}@example.test";

        var targetEmail =
            $"deactivate-target-{Guid.NewGuid():N}@example.test";

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        try
        {
            using var factory =
                AuthTestFactory
                    .CreateWithProductionTokenStateValidation();

            using (var scope =
                factory.Services.CreateScope())
            {
                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    actorEmail,
                    password,
                    AppRoles.Admin,
                    isActive: true,
                    userId: actorId);

                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    targetEmail,
                    password,
                    AppRoles.Operator,
                    isActive: true,
                    userId: targetId);
            }

            using var actorClient =
                AuthTestClientFactory.Create(factory);

            using var targetClient =
                AuthTestClientFactory.Create(factory);

            async Task<string> LoginAsync(
                HttpClient client,
                string email)
            {
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

                var json =
                    await response.Content
                        .ReadFromJsonAsync<JsonElement>();

                return json
                    .GetProperty("accessToken")
                    .GetString()!;
            }

            actorClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    await LoginAsync(
                        actorClient,
                        actorEmail));

            targetClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    await LoginAsync(
                        targetClient,
                        targetEmail));

            var deactivateResponse =
                await actorClient.PostAsync(
                    $"/api/admin/users/{targetId}/deactivate",
                    content: null);

            Assert.Equal(
                HttpStatusCode.NoContent,
                deactivateResponse.StatusCode);

            var staleTokenResponse =
                await targetClient.GetAsync(
                    "/api/products");

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                staleTokenResponse.StatusCode);

            await using (var reactivationContext =
                _database.CreateAuthDbContext())
            {
                await reactivationContext.Users
                    .Where(x => x.Id == targetId)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters.SetProperty(
                                x => x.IsActive,
                                true));
            }

            var reactivatedStaleTokenResponse =
                await targetClient.GetAsync(
                    "/api/products");

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                reactivatedStaleTokenResponse.StatusCode);
        }
        finally
        {
            await using var cleanup =
                _database.CreateAuthDbContext();

            var userIds =
                new[] { actorId, targetId };

            await cleanup.RefreshTokens
                .Where(x => userIds.Contains(x.UserId))
                .ExecuteDeleteAsync();

            await cleanup.Users
                .Where(x => userIds.Contains(x.Id))
                .ExecuteDeleteAsync();
        }
    }

}
