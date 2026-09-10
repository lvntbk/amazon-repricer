using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class AdminUserLifecyclePostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public AdminUserLifecyclePostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task CreateUser_AsAdmin_CreatesActiveUserWithRequestedRole()
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
            "Created-User-2026!";

        var email =
            $"admin-created-{Guid.NewGuid():N}@example.test";

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
            }

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    CreateToken(
                        issuer,
                        audience,
                        signingKey,
                        AppRoles.Admin));

            var response =
                await client.PostAsJsonAsync(
                    "/api/admin/users",
                    new
                    {
                        Email = email,
                        Password = password,
                        Role = AppRoles.Operator
                    });

            Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode);

            using var verificationScope =
                factory.Services.CreateScope();

            var userManager =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        UserManager<AppUser>>();

            var createdUser =
                await userManager.FindByEmailAsync(
                    email);

            Assert.NotNull(createdUser);

            Assert.True(
                createdUser.IsActive);

            Assert.True(
                await userManager.CheckPasswordAsync(
                    createdUser,
                    password));

            var roles =
                await userManager.GetRolesAsync(
                    createdUser);

            Assert.Equal(
                [AppRoles.Operator],
                roles);
        }
        finally
        {
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

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(AppRoles.Operator, HttpStatusCode.Forbidden)]
    public async Task CreateUser_WithoutAdminRole_IsRejectedAndDoesNotCreateUser(
        string? callerRole,
        HttpStatusCode expectedStatusCode)
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

        var email =
            $"unauthorized-create-{Guid.NewGuid():N}@example.test";

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

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            if (callerRole is not null)
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        CreateToken(
                            issuer,
                            audience,
                            signingKey,
                            callerRole));
            }

            var response =
                await client.PostAsJsonAsync(
                    "/api/admin/users",
                    new
                    {
                        Email = email,
                        Password = "Created-User-2026!",
                        Role = AppRoles.Operator
                    });

            Assert.Equal(
                expectedStatusCode,
                response.StatusCode);

            using var verificationScope =
                factory.Services.CreateScope();

            var userManager =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        UserManager<AppUser>>();

            var createdUser =
                await userManager.FindByEmailAsync(
                    email);

            Assert.Null(createdUser);
        }
        finally
        {
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
    public async Task DeactivateUser_AsAdmin_DeactivatesUserAndRevokesActiveRefreshTokens()
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
            "Target-User-2026!";

        var email =
            $"deactivate-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

        var refreshTokenId =
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

            var tokenCreatedAtUtc =
                DateTime.UtcNow.AddMinutes(-1);

            await using (var seedContext =
                _database.CreateAuthDbContext())
            {
                seedContext.RefreshTokens.Add(
                    new AuthRefreshToken
                    {
                        Id = refreshTokenId,
                        UserId = userId,
                        FamilyId = Guid.NewGuid(),
                        TokenHash =
                            Convert.ToHexString(
                                System.Security.Cryptography
                                    .SHA256.HashData(
                                        Encoding.UTF8.GetBytes(
                                            Guid.NewGuid()
                                                .ToString("N")))),
                        CreatedAtUtc =
                            tokenCreatedAtUtc,
                        ExpiresAtUtc =
                            tokenCreatedAtUtc.AddDays(30)
                    });

                await seedContext.SaveChangesAsync();
            }

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    CreateToken(
                        issuer,
                        audience,
                        signingKey,
                        AppRoles.Admin));

            var response =
                await client.PostAsync(
                    $"/api/admin/users/{userId}/deactivate",
                    content: null);

            Assert.Equal(
                HttpStatusCode.NoContent,
                response.StatusCode);

            using (var verificationScope =
                factory.Services.CreateScope())
            {
                var userManager =
                    verificationScope.ServiceProvider
                        .GetRequiredService<
                            UserManager<AppUser>>();

                var user =
                    await userManager.FindByIdAsync(
                        userId.ToString());

                Assert.NotNull(user);
                Assert.False(user.IsActive);
            }

            await using var verificationContext =
                _database.CreateAuthDbContext();

            var refreshToken =
                await verificationContext.RefreshTokens
                    .FindAsync(refreshTokenId);

            Assert.NotNull(refreshToken);
            Assert.NotNull(refreshToken.RevokedAtUtc);

            Assert.Equal(
                "UserDeactivated",
                refreshToken.RevocationReason);
        }
        finally
        {
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
    public async Task DeactivateUser_WhenUserAlreadyInactive_StillRevokesActiveRefreshTokens()
    {
        const string connectionStringVariable =
            "ConnectionStrings__DefaultConnection";

        const string issuerVariable =
            "Jwt__Issuer";

        const string audienceVariable =
            "Jwt__Audience";

        const string signingKeyVariable =
            "Jwt__SigningKey";

        const string bootstrapEnabledVariable =
            "AuthBootstrap__Enabled";

        const string issuer =
            "AmazonRepricer.IntegrationTests";

        const string audience =
            "AmazonRepricer.IntegrationTests";

        const string signingKey =
            "integration-test-signing-key-32-bytes-minimum";

        const string password =
            "Inactive-Target-2026!";

        var email =
            $"inactive-deactivate-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

        var refreshTokenId =
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

        var originalBootstrapEnabled =
            Environment.GetEnvironmentVariable(
                bootstrapEnabledVariable);

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

            Environment.SetEnvironmentVariable(
                bootstrapEnabledVariable,
                "false");

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

            var tokenCreatedAtUtc =
                DateTime.UtcNow.AddMinutes(-1);

            await using (var seedContext =
                _database.CreateAuthDbContext())
            {
                seedContext.RefreshTokens.Add(
                    new AuthRefreshToken
                    {
                        Id = refreshTokenId,
                        UserId = userId,
                        FamilyId = Guid.NewGuid(),
                        TokenHash =
                            Convert.ToHexString(
                                System.Security.Cryptography
                                    .SHA256.HashData(
                                        Encoding.UTF8.GetBytes(
                                            Guid.NewGuid()
                                                .ToString("N")))),
                        CreatedAtUtc =
                            tokenCreatedAtUtc,
                        ExpiresAtUtc =
                            tokenCreatedAtUtc.AddDays(30)
                    });

                await seedContext.SaveChangesAsync();
            }

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    CreateToken(
                        issuer,
                        audience,
                        signingKey,
                        AppRoles.Admin));

            var response =
                await client.PostAsync(
                    $"/api/admin/users/{userId}/deactivate",
                    content: null);

            Assert.Equal(
                HttpStatusCode.NoContent,
                response.StatusCode);

            await using var verificationContext =
                _database.CreateAuthDbContext();

            var refreshToken =
                await verificationContext.RefreshTokens
                    .FindAsync(refreshTokenId);

            Assert.NotNull(refreshToken);
            Assert.NotNull(refreshToken.RevokedAtUtc);

            Assert.Equal(
                "UserDeactivated",
                refreshToken.RevocationReason);
        }
        finally
        {
            await using (var cleanupContext =
                _database.CreateAuthDbContext())
            {
                await cleanupContext.Users
                    .Where(x => x.Id == userId)
                    .ExecuteDeleteAsync();
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

            Environment.SetEnvironmentVariable(
                bootstrapEnabledVariable,
                originalBootstrapEnabled);
        }
    }

    [Fact]
    public async Task DeactivateUser_WhenUserIsLastActiveAdmin_IsRejected()
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
            "Last-Admin-2026!";

        var email =
            $"last-admin-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

        var refreshTokenId =
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
                    AppRoles.Admin))
                {
                    var createRoleResult =
                        await roleManager.CreateAsync(
                            new IdentityRole<Guid>(
                                AppRoles.Admin));

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

                var roleResult =
                    await userManager.AddToRoleAsync(
                        user,
                        AppRoles.Admin);

                Assert.True(
                    roleResult.Succeeded,
                    string.Join(
                        "; ",
                        roleResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));

                var admins =
                    await userManager.GetUsersInRoleAsync(
                        AppRoles.Admin);

                Assert.Single(
                    admins,
                    x => x.IsActive);
            }

            var tokenCreatedAtUtc =
                DateTime.UtcNow.AddMinutes(-1);

            await using (var seedContext =
                _database.CreateAuthDbContext())
            {
                seedContext.RefreshTokens.Add(
                    new AuthRefreshToken
                    {
                        Id = refreshTokenId,
                        UserId = userId,
                        FamilyId = Guid.NewGuid(),
                        TokenHash =
                            Convert.ToHexString(
                                System.Security.Cryptography
                                    .SHA256.HashData(
                                        Encoding.UTF8.GetBytes(
                                            Guid.NewGuid()
                                                .ToString("N")))),
                        CreatedAtUtc =
                            tokenCreatedAtUtc,
                        ExpiresAtUtc =
                            tokenCreatedAtUtc.AddDays(30)
                    });

                await seedContext.SaveChangesAsync();
            }

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    CreateToken(
                        issuer,
                        audience,
                        signingKey,
                        AppRoles.Admin));

            var response =
                await client.PostAsync(
                    $"/api/admin/users/{userId}/deactivate",
                    content: null);

            Assert.Equal(
                HttpStatusCode.Conflict,
                response.StatusCode);

            using (var verificationScope =
                factory.Services.CreateScope())
            {
                var userManager =
                    verificationScope.ServiceProvider
                        .GetRequiredService<
                            UserManager<AppUser>>();

                var user =
                    await userManager.FindByIdAsync(
                        userId.ToString());

                Assert.NotNull(user);
                Assert.True(user.IsActive);
            }

            await using var verificationContext =
                _database.CreateAuthDbContext();

            var refreshToken =
                await verificationContext.RefreshTokens
                    .FindAsync(refreshTokenId);

            Assert.NotNull(refreshToken);
            Assert.Null(refreshToken.RevokedAtUtc);
            Assert.Null(refreshToken.RevocationReason);
        }
        finally
        {
            await using (var cleanupContext =
                _database.CreateAuthDbContext())
            {
                await cleanupContext.Users
                    .Where(x => x.Id == userId)
                    .ExecuteDeleteAsync();
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
    public async Task ChangeUserRole_AsAdmin_ChangesRoleAndRevokesActiveRefreshTokens()
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
            "Role-Target-2026!";

        var email =
            $"role-change-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

        var refreshTokenId =
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

                foreach (var role in new[]
                {
                    AppRoles.Admin,
                    AppRoles.Operator
                })
                {
                    if (!await roleManager.RoleExistsAsync(role))
                    {
                        var createRoleResult =
                            await roleManager.CreateAsync(
                                new IdentityRole<Guid>(role));

                        Assert.True(
                            createRoleResult.Succeeded,
                            string.Join(
                                "; ",
                                createRoleResult.Errors.Select(
                                    x =>
                                        $"{x.Code}: {x.Description}")));
                    }
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

            var tokenCreatedAtUtc =
                DateTime.UtcNow.AddMinutes(-1);

            await using (var seedContext =
                _database.CreateAuthDbContext())
            {
                seedContext.RefreshTokens.Add(
                    new AuthRefreshToken
                    {
                        Id = refreshTokenId,
                        UserId = userId,
                        FamilyId = Guid.NewGuid(),
                        TokenHash =
                            Convert.ToHexString(
                                System.Security.Cryptography
                                    .SHA256.HashData(
                                        Encoding.UTF8.GetBytes(
                                            Guid.NewGuid()
                                                .ToString("N")))),
                        CreatedAtUtc =
                            tokenCreatedAtUtc,
                        ExpiresAtUtc =
                            tokenCreatedAtUtc.AddDays(30)
                    });

                await seedContext.SaveChangesAsync();
            }

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    CreateToken(
                        issuer,
                        audience,
                        signingKey,
                        AppRoles.Admin));

            var response =
                await client.PutAsJsonAsync(
                    $"/api/admin/users/{userId}/role",
                    new
                    {
                        Role = AppRoles.Admin
                    });

            Assert.Equal(
                HttpStatusCode.NoContent,
                response.StatusCode);

            using (var verificationScope =
                factory.Services.CreateScope())
            {
                var userManager =
                    verificationScope.ServiceProvider
                        .GetRequiredService<
                            UserManager<AppUser>>();

                var user =
                    await userManager.FindByIdAsync(
                        userId.ToString());

                Assert.NotNull(user);

                var roles =
                    await userManager.GetRolesAsync(
                        user);

                Assert.Equal(
                    [AppRoles.Admin],
                    roles);
            }

            await using var verificationContext =
                _database.CreateAuthDbContext();

            var refreshToken =
                await verificationContext.RefreshTokens
                    .FindAsync(refreshTokenId);

            Assert.NotNull(refreshToken);
            Assert.NotNull(refreshToken.RevokedAtUtc);

            Assert.Equal(
                "RoleChanged",
                refreshToken.RevocationReason);
        }
        finally
        {
            await using (var cleanupContext =
                _database.CreateAuthDbContext())
            {
                await cleanupContext.Users
                    .Where(x => x.Id == userId)
                    .ExecuteDeleteAsync();
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
    public async Task ChangeUserRole_WhenUserIsLastActiveAdmin_IsRejected()
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
            "Last-Admin-Role-2026!";

        var email =
            $"last-admin-role-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

        var refreshTokenId =
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

                foreach (var role in new[]
                {
                    AppRoles.Admin,
                    AppRoles.Operator
                })
                {
                    if (!await roleManager.RoleExistsAsync(role))
                    {
                        var createRoleResult =
                            await roleManager.CreateAsync(
                                new IdentityRole<Guid>(role));

                        Assert.True(
                            createRoleResult.Succeeded,
                            string.Join(
                                "; ",
                                createRoleResult.Errors.Select(
                                    x =>
                                        $"{x.Code}: {x.Description}")));
                    }
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

                var roleResult =
                    await userManager.AddToRoleAsync(
                        user,
                        AppRoles.Admin);

                Assert.True(
                    roleResult.Succeeded,
                    string.Join(
                        "; ",
                        roleResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));

                var admins =
                    await userManager.GetUsersInRoleAsync(
                        AppRoles.Admin);

                Assert.Single(
                    admins,
                    x => x.IsActive);
            }

            var tokenCreatedAtUtc =
                DateTime.UtcNow.AddMinutes(-1);

            await using (var seedContext =
                _database.CreateAuthDbContext())
            {
                seedContext.RefreshTokens.Add(
                    new AuthRefreshToken
                    {
                        Id = refreshTokenId,
                        UserId = userId,
                        FamilyId = Guid.NewGuid(),
                        TokenHash =
                            Convert.ToHexString(
                                System.Security.Cryptography
                                    .SHA256.HashData(
                                        Encoding.UTF8.GetBytes(
                                            Guid.NewGuid()
                                                .ToString("N")))),
                        CreatedAtUtc =
                            tokenCreatedAtUtc,
                        ExpiresAtUtc =
                            tokenCreatedAtUtc.AddDays(30)
                    });

                await seedContext.SaveChangesAsync();
            }

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    CreateToken(
                        issuer,
                        audience,
                        signingKey,
                        AppRoles.Admin));

            var response =
                await client.PutAsJsonAsync(
                    $"/api/admin/users/{userId}/role",
                    new
                    {
                        Role = AppRoles.Operator
                    });

            Assert.Equal(
                HttpStatusCode.Conflict,
                response.StatusCode);

            using (var verificationScope =
                factory.Services.CreateScope())
            {
                var userManager =
                    verificationScope.ServiceProvider
                        .GetRequiredService<
                            UserManager<AppUser>>();

                var user =
                    await userManager.FindByIdAsync(
                        userId.ToString());

                Assert.NotNull(user);

                var roles =
                    await userManager.GetRolesAsync(
                        user);

                Assert.Equal(
                    [AppRoles.Admin],
                    roles);
            }

            await using var verificationContext =
                _database.CreateAuthDbContext();

            var refreshToken =
                await verificationContext.RefreshTokens
                    .FindAsync(refreshTokenId);

            Assert.NotNull(refreshToken);
            Assert.Null(refreshToken.RevokedAtUtc);
            Assert.Null(refreshToken.RevocationReason);
        }
        finally
        {
            await using (var cleanupContext =
                _database.CreateAuthDbContext())
            {
                await cleanupContext.Users
                    .Where(x => x.Id == userId)
                    .ExecuteDeleteAsync();
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

    [Theory]
    [InlineData("short", AppRoles.Operator)]
    [InlineData("Created-User-2026!", "Viewer")]
    public async Task CreateUser_WithInvalidPasswordOrRole_IsRejectedAndDoesNotCreateUser(
        string password,
        string role)
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

        var email =
            $"invalid-create-{Guid.NewGuid():N}@example.test";

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
            }

            using var client =
                factory.CreateClient(
                    new WebApplicationFactoryClientOptions
                    {
                        AllowAutoRedirect = false
                    });

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    CreateToken(
                        issuer,
                        audience,
                        signingKey,
                        AppRoles.Admin));

            var response =
                await client.PostAsJsonAsync(
                    "/api/admin/users",
                    new
                    {
                        Email = email,
                        Password = password,
                        Role = role
                    });

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            using var verificationScope =
                factory.Services.CreateScope();

            var userManager =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        UserManager<AppUser>>();

            var createdUser =
                await userManager.FindByEmailAsync(
                    email);

            Assert.Null(createdUser);
        }
        finally
        {
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
    public async Task CreateUser_WithDuplicateEmail_IsRejectedAndDoesNotCreateSecondUser()
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

        const string existingPassword =
            "Existing-User-2026!";

        const string duplicatePassword =
            "Duplicate-User-2026!";

        var email =
            $"duplicate-{Guid.NewGuid():N}@example.test";

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

                foreach (var role in new[]
                {
                    AppRoles.Admin,
                    AppRoles.Operator
                })
                {
                    if (!await roleManager.RoleExistsAsync(role))
                    {
                        var createRoleResult =
                            await roleManager.CreateAsync(
                                new IdentityRole<Guid>(role));

                        Assert.True(
                            createRoleResult.Succeeded,
                            string.Join(
                                "; ",
                                createRoleResult.Errors.Select(
                                    x =>
                                        $"{x.Code}: {x.Description}")));
                    }
                }

                var seedUserManager =
                    scope.ServiceProvider
                        .GetRequiredService<
                            UserManager<AppUser>>();

                var existingUser =
                    new AppUser
                    {
                        Id = Guid.NewGuid(),
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true,
                        IsActive = true
                    };

                var createResult =
                    await seedUserManager.CreateAsync(
                        existingUser,
                        existingPassword);

                Assert.True(
                    createResult.Succeeded,
                    string.Join(
                        "; ",
                        createResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));

                var roleResult =
                    await seedUserManager.AddToRoleAsync(
                        existingUser,
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

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    CreateToken(
                        issuer,
                        audience,
                        signingKey,
                        AppRoles.Admin));

            var response =
                await client.PostAsJsonAsync(
                    "/api/admin/users",
                    new
                    {
                        Email = email,
                        Password = duplicatePassword,
                        Role = AppRoles.Admin
                    });

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            using var verificationScope =
                factory.Services.CreateScope();

            var userManager =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        UserManager<AppUser>>();

            var usersWithEmail =
                userManager.Users
                    .Where(x => x.Email == email)
                    .ToList();

            Assert.Single(usersWithEmail);

            var persistedUser =
                usersWithEmail[0];

            Assert.True(
                await userManager.CheckPasswordAsync(
                    persistedUser,
                    existingPassword));

            Assert.False(
                await userManager.CheckPasswordAsync(
                    persistedUser,
                    duplicatePassword));

            var roles =
                await userManager.GetRolesAsync(
                    persistedUser);

            Assert.Equal(
                [AppRoles.Operator],
                roles);
        }
        finally
        {
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
