using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class LoginPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public LoginPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessAndRefreshTokensAndPersistsOnlyRefreshTokenHash()
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

        const string password =
            "Correct-Horse-2026!";

        var suffix =
            Guid.NewGuid().ToString("N");

        var email =
            $"login-{suffix}@example.test";

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
                issuer);

            Environment.SetEnvironmentVariable(
                audienceVariable,
                audience);

            Environment.SetEnvironmentVariable(
                signingKeyVariable,
                signingKey);

            using var factory =
                new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(
                        builder =>
                            builder.UseEnvironment("Testing"));

            using (var scope =
                factory.Services.CreateScope())
            {
                var roleManager =
                    scope.ServiceProvider
                        .GetRequiredService<
                            RoleManager<IdentityRole<Guid>>>();

                if (!await roleManager.RoleExistsAsync(
                    AppRoles.Operator))
                {
                    var createRoleResult =
                        await roleManager.CreateAsync(
                            new IdentityRole<Guid>(
                                AppRoles.Operator));

                    Assert.True(
                        createRoleResult.Succeeded,
                        string.Join(
                            "; ",
                            createRoleResult.Errors.Select(
                                x =>
                                    $"{x.Code}: {x.Description}")));
                }

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

                var addToRoleResult =
                    await userManager.AddToRoleAsync(
                        user,
                        AppRoles.Operator);

                Assert.True(
                    addToRoleResult.Succeeded,
                    string.Join(
                        "; ",
                        addToRoleResult.Errors.Select(
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

            Assert.False(
                string.IsNullOrWhiteSpace(
                    payload.AccessToken));

            Assert.Equal(
                "Bearer",
                payload.TokenType);

            Assert.InRange(
                payload.ExpiresAtUtc,
                beforeLogin.AddMinutes(14),
                beforeLogin.AddMinutes(16));

            Assert.False(
                string.IsNullOrWhiteSpace(
                    payload.RefreshToken));

            Assert.Equal(
                64,
                payload.RefreshToken.Length);

            Assert.True(
                payload.RefreshToken.All(
                    Uri.IsHexDigit));

            Assert.True(
                payload.RefreshTokenExpiresAtUtc >
                payload.ExpiresAtUtc);

            var expectedRefreshTokenHash =
                Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            payload.RefreshToken)));

            await using (var refreshTokenVerification =
                _database.CreateAuthDbContext())
            {
                var persistedRefreshToken =
                    await refreshTokenVerification
                        .RefreshTokens
                        .SingleAsync(
                            x => x.UserId == userId);

                Assert.Equal(
                    expectedRefreshTokenHash,
                    persistedRefreshToken.TokenHash);

                Assert.NotEqual(
                    payload.RefreshToken,
                    persistedRefreshToken.TokenHash);

                Assert.NotEqual(
                    Guid.Empty,
                    persistedRefreshToken.FamilyId);

                Assert.True(
                    persistedRefreshToken.ExpiresAtUtc >
                    persistedRefreshToken.CreatedAtUtc);

                Assert.Null(
                    persistedRefreshToken.RevokedAtUtc);

                Assert.Null(
                    persistedRefreshToken.ReplacedByTokenId);
            }

            var tokenHandler =
                new JwtSecurityTokenHandler();

            var principal =
                tokenHandler.ValidateToken(
                    payload.AccessToken,
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = issuer,
                        ValidateAudience = true,
                        ValidAudience = audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey =
                            new SymmetricSecurityKey(
                                Encoding.UTF8.GetBytes(
                                    signingKey)),
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero
                    },
                    out var validatedToken);

            Assert.IsType<JwtSecurityToken>(
                validatedToken);

            Assert.Equal(
                userId.ToString(),
                principal.FindFirstValue(
                    ClaimTypes.NameIdentifier));

            Assert.True(
                principal.IsInRole(
                    AppRoles.Operator));

            var jwt =
                (JwtSecurityToken)validatedToken;

            Assert.Equal(
                issuer,
                jwt.Issuer);

            Assert.Contains(
                audience,
                jwt.Audiences);

            Assert.InRange(
                new DateTimeOffset(
                    jwt.ValidTo,
                    TimeSpan.Zero),
                beforeLogin.AddMinutes(14),
                beforeLogin.AddMinutes(16));

            Guid originalRefreshTokenId;
            Guid originalFamilyId;

            await using (var beforeRefreshContext =
                _database.CreateAuthDbContext())
            {
                var originalRefreshToken =
                    await beforeRefreshContext
                        .RefreshTokens
                        .SingleAsync(
                            x => x.UserId == userId);

                originalRefreshTokenId =
                    originalRefreshToken.Id;

                originalFamilyId =
                    originalRefreshToken.FamilyId;
            }

            var refreshResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new
                    {
                        RefreshToken =
                            payload.RefreshToken
                    });

            Assert.Equal(
                HttpStatusCode.OK,
                refreshResponse.StatusCode);

            var refreshedPayload =
                await refreshResponse.Content
                    .ReadFromJsonAsync<LoginResponse>();

            Assert.NotNull(
                refreshedPayload);

            Assert.False(
                string.IsNullOrWhiteSpace(
                    refreshedPayload.AccessToken));

            Assert.NotEqual(
                payload.AccessToken,
                refreshedPayload.AccessToken);

            Assert.False(
                string.IsNullOrWhiteSpace(
                    refreshedPayload.RefreshToken));

            Assert.NotEqual(
                payload.RefreshToken,
                refreshedPayload.RefreshToken);

            await using (var afterRefreshContext =
                _database.CreateAuthDbContext())
            {
                var refreshTokens =
                    await afterRefreshContext
                        .RefreshTokens
                        .Where(
                            x => x.UserId == userId)
                        .OrderBy(
                            x => x.CreatedAtUtc)
                        .ToListAsync();

                Assert.Equal(
                    2,
                    refreshTokens.Count);

                var original =
                    refreshTokens.Single(
                        x =>
                            x.Id ==
                            originalRefreshTokenId);

                var replacement =
                    refreshTokens.Single(
                        x =>
                            x.Id !=
                            originalRefreshTokenId);

                Assert.NotNull(
                    original.RevokedAtUtc);

                Assert.Equal(
                    replacement.Id,
                    original.ReplacedByTokenId);

                Assert.Equal(
                    originalFamilyId,
                    replacement.FamilyId);

                Assert.Null(
                    replacement.RevokedAtUtc);

                Assert.Null(
                    replacement.ReplacedByTokenId);

                Assert.NotEqual(
                    original.TokenHash,
                    replacement.TokenHash);
            }

            var replayResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new
                    {
                        RefreshToken =
                            payload.RefreshToken
                    });

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                replayResponse.StatusCode);

            await using (var replayVerificationContext =
                _database.CreateAuthDbContext())
            {
                var familyTokens =
                    await replayVerificationContext
                        .RefreshTokens
                        .Where(
                            x =>
                                x.UserId == userId &&
                                x.FamilyId == originalFamilyId)
                        .ToListAsync();

                Assert.Equal(
                    2,
                    familyTokens.Count);

                Assert.All(
                    familyTokens,
                    token =>
                        Assert.NotNull(
                            token.RevokedAtUtc));

                var replacementAfterReplay =
                    familyTokens.Single(
                        x =>
                            x.Id != originalRefreshTokenId);

                Assert.Equal(
                    "ReplayDetected",
                    replacementAfterReplay.RevocationReason);
            }
        }
        finally
        {
            await using var cleanup =
                _database.CreateAuthDbContext();

            var persistedUser =
                await cleanup.Users.FindAsync(userId);

            if (persistedUser is not null)
            {
                cleanup.Users.Remove(persistedUser);
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
