using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AmazonRepricer.Infrastructure.Identity;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class LoginFailurePostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public LoginFailurePostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Login_AfterFourWrongPasswords_LocksUserAndRejectsCorrectPassword()
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

        const string wrongPassword =
            "Wrong-Horse-2026!";

        var suffix =
            Guid.NewGuid().ToString("N");

        var email =
            $"lockout-login-{suffix}@example.test";

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
                        .GetRequiredService<UserManager<AppUser>>();

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

            var beforeFailures =
                DateTimeOffset.UtcNow;

            for (var attempt = 1; attempt <= 4; attempt++)
            {
                var response =
                    await client.PostAsJsonAsync(
                        "/api/auth/login",
                        new
                        {
                            Email = email,
                            Password = wrongPassword
                        });

                Assert.Equal(
                    HttpStatusCode.Unauthorized,
                    response.StatusCode);
            }

            await using (var verificationContext =
                _database.CreateAuthDbContext())
            {
                var persistedUser =
                    await verificationContext.Users
                        .SingleAsync(
                            x => x.Id == userId);

                Assert.NotNull(
                    persistedUser.LockoutEnd);

                Assert.InRange(
                    persistedUser.LockoutEnd!.Value,
                    beforeFailures.AddMinutes(9),
                    beforeFailures.AddMinutes(11));
            }

            var correctPasswordResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new
                    {
                        Email = email,
                        Password = password
                    });

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                correctPasswordResponse.StatusCode);

            await using var finalVerificationContext =
                _database.CreateAuthDbContext();

            var lockedUser =
                await finalVerificationContext.Users
                    .SingleAsync(
                        x => x.Id == userId);

            Assert.NotNull(
                lockedUser.LockoutEnd);

            Assert.True(
                lockedUser.LockoutEnd >
                DateTimeOffset.UtcNow);
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


    [Fact]
    public async Task Login_WithInactiveUser_ReturnsUnauthorizedWithoutIncrementingFailedCount()
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

        var suffix =
            Guid.NewGuid().ToString("N");

        var email =
            $"inactive-login-{suffix}@example.test";

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
                        .GetRequiredService<UserManager<AppUser>>();

                var user =
                    new AppUser
                    {
                        Id = userId,
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true,
                        IsActive = false
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

            var response =
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new
                    {
                        Email = email,
                        Password = password
                    });

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);

            await using var verificationContext =
                _database.CreateAuthDbContext();

            var persistedUser =
                await verificationContext.Users
                    .SingleAsync(
                        x => x.Id == userId);

            Assert.False(
                persistedUser.IsActive);

            Assert.Equal(
                0,
                persistedUser.AccessFailedCount);

            Assert.Null(
                persistedUser.LockoutEnd);
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


    [Fact]
    public async Task Login_WithUnknownEmail_AndWrongPassword_ReturnSamePublicResponse()
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

        const string wrongPassword =
            "Wrong-Horse-2026!";

        var suffix =
            Guid.NewGuid().ToString("N");

        var existingEmail =
            $"enumeration-{suffix}@example.test";

        var unknownEmail =
            $"unknown-{suffix}@example.test";

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
                        .GetRequiredService<UserManager<AppUser>>();

                var user =
                    new AppUser
                    {
                        Id = userId,
                        UserName = existingEmail,
                        Email = existingEmail,
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

            var knownUserResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new
                    {
                        Email = existingEmail,
                        Password = wrongPassword
                    });

            var unknownUserResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new
                    {
                        Email = unknownEmail,
                        Password = wrongPassword
                    });

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                knownUserResponse.StatusCode);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                unknownUserResponse.StatusCode);

            var knownBody =
                await knownUserResponse.Content
                    .ReadAsStringAsync();

            var unknownBody =
                await unknownUserResponse.Content
                    .ReadAsStringAsync();

            using var knownJson =
                JsonDocument.Parse(knownBody);

            using var unknownJson =
                JsonDocument.Parse(unknownBody);

            var knownRoot =
                knownJson.RootElement;

            var unknownRoot =
                unknownJson.RootElement;

            Assert.Equal(
                knownRoot.GetProperty("status").GetInt32(),
                unknownRoot.GetProperty("status").GetInt32());

            Assert.Equal(
                knownRoot.GetProperty("title").GetString(),
                unknownRoot.GetProperty("title").GetString());

            Assert.Equal(
                knownRoot.GetProperty("type").GetString(),
                unknownRoot.GetProperty("type").GetString());

            Assert.DoesNotContain(
                existingEmail,
                knownBody,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                unknownEmail,
                unknownBody,
                StringComparison.OrdinalIgnoreCase);
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


    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorizedAndIncrementsFailedCount()
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

        const string wrongPassword =
            "Wrong-Horse-2026!";

        var suffix =
            Guid.NewGuid().ToString("N");

        var email =
            $"wrong-login-{suffix}@example.test";

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

            var response =
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new
                    {
                        Email = email,
                        Password = wrongPassword
                    });

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);

            await using var verificationContext =
                _database.CreateAuthDbContext();

            var persistedUser =
                await verificationContext.Users
                    .SingleAsync(
                        x => x.Id == userId);

            Assert.Equal(
                1,
                persistedUser.AccessFailedCount);

            Assert.Null(
                persistedUser.LockoutEnd);

            Assert.True(
                persistedUser.IsActive);
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
}
