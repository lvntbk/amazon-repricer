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
public sealed class RefreshConcurrencyPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public RefreshConcurrencyPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Refresh_SameTokenConcurrently_AllowsSingleWinnerAndRevokesFamily()
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
            $"refresh-concurrency-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

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
                            builder.UseEnvironment("Testing"));

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

            Guid familyId;

            await using (var beforeContext =
                _database.CreateAuthDbContext())
            {
                var originalToken =
                    await beforeContext
                        .RefreshTokens
                        .SingleAsync(
                            x => x.UserId == userId);

                familyId =
                    originalToken.FamilyId;
            }

            var startGate =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions
                        .RunContinuationsAsynchronously);

            async Task<HttpResponseMessage> SendRefreshAsync()
            {
                await startGate.Task;

                return await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new
                    {
                        RefreshToken =
                            loginPayload.RefreshToken
                    });
            }

            var firstTask =
                SendRefreshAsync();

            var secondTask =
                SendRefreshAsync();

            startGate.SetResult(true);

            var responses =
                await Task.WhenAll(
                    firstTask,
                    secondTask);

            try
            {
                Assert.Equal(
                    1,
                    responses.Count(
                        x =>
                            x.StatusCode ==
                            HttpStatusCode.OK));

                Assert.Equal(
                    1,
                    responses.Count(
                        x =>
                            x.StatusCode ==
                            HttpStatusCode.Unauthorized));
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }

            await using var verificationContext =
                _database.CreateAuthDbContext();

            var familyTokens =
                await verificationContext
                    .RefreshTokens
                    .Where(
                        x =>
                            x.UserId == userId &&
                            x.FamilyId == familyId)
                    .ToListAsync();

            Assert.Equal(
                2,
                familyTokens.Count);

            Assert.All(
                familyTokens,
                token =>
                    Assert.NotNull(
                        token.RevokedAtUtc));

            Assert.DoesNotContain(
                familyTokens,
                token =>
                    token.RevokedAtUtc is null);

            var replacementToken =
                familyTokens.Single(
                    x =>
                        x.ReplacedByTokenId is null);

            Assert.Equal(
                "ReplayDetected",
                replacementToken.RevocationReason);
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

    private sealed record LoginResponse(
        string AccessToken,
        string TokenType,
        DateTimeOffset ExpiresAtUtc,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAtUtc);
}
