using System.Net;
using System.Net.Http.Json;
using System.Text;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

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
        const string password =
            "Created-User-2026!";

        var email =
            $"admin-created-{Guid.NewGuid():N}@example.test";

        using (AuthTestEnvironmentScope.CreateDefault(
            _database.ConnectionString))
        {
            using var factory =
                AuthTestFactory.Create();

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
                AuthTestClientFactory.CreateAuthenticated(
                    factory,
                    AppRoles.Admin);

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
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(AppRoles.Operator, HttpStatusCode.Forbidden)]
    public async Task CreateUser_WithoutAdminRole_IsRejectedAndDoesNotCreateUser(
        string? callerRole,
        HttpStatusCode expectedStatusCode)
    {
        var email =
            $"unauthorized-create-{Guid.NewGuid():N}@example.test";

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        try
        {
            using var factory =
                AuthTestFactory.Create();

            using var client =
                AuthTestClientFactory.Create(
                    factory,
                    callerRole);

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
        }
    }

    [Fact]
    public async Task DeactivateUser_AsAdmin_DeactivatesUserAndRevokesActiveRefreshTokens()
    {
        const string password =
            "Target-User-2026!";

        var email =
            $"deactivate-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

        var refreshTokenId =
            Guid.NewGuid();

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        try
        {
            using var factory =
                AuthTestFactory.Create();

            using (var scope =
                factory.Services.CreateScope())
            {
                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    email,
                    password,
                    AppRoles.Operator,
                    isActive: true,
                    userId: userId);
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
                AuthTestClientFactory.CreateAuthenticated(
                    factory,
                    AppRoles.Admin);

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
        }
    }


    [Fact]
    public async Task DeactivateUser_WhenUserAlreadyInactive_StillRevokesActiveRefreshTokens()
    {
        const string password =
            "Inactive-Target-2026!";

        var email =
            $"inactive-deactivate-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

        var refreshTokenId =
            Guid.NewGuid();

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString,
                ("AuthBootstrap__Enabled", "false"));

        try
        {
            using var factory =
                AuthTestFactory.Create();

            using (var scope =
                factory.Services.CreateScope())
            {
                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    email,
                    password,
                    AppRoles.Operator,
                    isActive: false,
                    userId: userId);
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
                AuthTestClientFactory.CreateAuthenticated(
                    factory,
                    AppRoles.Admin);

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

        }
    }

    [Fact]
    public async Task DeactivateUser_WhenUserIsLastActiveAdmin_IsRejected()
    {
        const string password =
            "Last-Admin-2026!";

        var email =
            $"last-admin-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

        var refreshTokenId =
            Guid.NewGuid();

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        try
        {
            using var factory =
                AuthTestFactory.Create();

            using (var scope =
                factory.Services.CreateScope())
            {
                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    email,
                    password,
                    AppRoles.Admin,
                    isActive: true,
                    userId: userId);

                var userManager =
                    scope.ServiceProvider
                        .GetRequiredService<
                            UserManager<AppUser>>();

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
                AuthTestClientFactory.CreateAuthenticated(
                    factory,
                    AppRoles.Admin);

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

        }
    }

    [Fact]
    public async Task ChangeUserRole_AsAdmin_ChangesRoleAndRevokesActiveRefreshTokens()
    {
        const string password =
            "Role-Target-2026!";

        var email =
            $"role-change-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

        var refreshTokenId =
            Guid.NewGuid();

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        try
        {
            using var factory =
                AuthTestFactory.Create();

            using (var scope =
                factory.Services.CreateScope())
            {
                await AuthTestData.EnsureRoleAsync(
                    scope.ServiceProvider,
                    AppRoles.Admin);

                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    email,
                    password,
                    AppRoles.Operator,
                    isActive: true,
                    userId: userId);
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
                AuthTestClientFactory.CreateAuthenticated(
                    factory,
                    AppRoles.Admin);

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

        }
    }

    [Fact]
    public async Task ChangeUserRole_WhenUserIsLastActiveAdmin_IsRejected()
    {
        const string password =
            "Last-Admin-Role-2026!";

        var email =
            $"last-admin-role-{Guid.NewGuid():N}@example.test";

        var userId =
            Guid.NewGuid();

        var refreshTokenId =
            Guid.NewGuid();

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        try
        {
            using var factory =
                AuthTestFactory.Create();

            using (var scope =
                factory.Services.CreateScope())
            {
                await AuthTestData.EnsureRoleAsync(
                    scope.ServiceProvider,
                    AppRoles.Operator);

                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    email,
                    password,
                    AppRoles.Admin,
                    isActive: true,
                    userId: userId);

                var userManager =
                    scope.ServiceProvider
                        .GetRequiredService<
                            UserManager<AppUser>>();

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
                AuthTestClientFactory.CreateAuthenticated(
                    factory,
                    AppRoles.Admin);

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

        }
    }

    [Theory]
    [InlineData("short", AppRoles.Operator)]
    [InlineData("Created-User-2026!", "Viewer")]
    public async Task CreateUser_WithInvalidPasswordOrRole_IsRejectedAndDoesNotCreateUser(
        string password,
        string role)
    {
        var email =
            $"invalid-create-{Guid.NewGuid():N}@example.test";

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        try
        {
            using var factory =
                AuthTestFactory.Create();

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
                AuthTestClientFactory.CreateAuthenticated(
                    factory,
                    AppRoles.Admin);

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
        }
    }

    [Fact]
    public async Task CreateUser_WithDuplicateEmail_IsRejectedAndDoesNotCreateSecondUser()
    {
        const string existingPassword =
            "Existing-User-2026!";

        const string duplicatePassword =
            "Duplicate-User-2026!";

        var email =
            $"duplicate-{Guid.NewGuid():N}@example.test";

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString);

        try
        {
            using var factory =
                AuthTestFactory.Create();

            using (var scope =
                factory.Services.CreateScope())
            {
                await AuthTestData.EnsureRoleAsync(
                    scope.ServiceProvider,
                    AppRoles.Admin);

                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    email,
                    existingPassword,
                    AppRoles.Operator);
            }

            using var client =
                AuthTestClientFactory.CreateAuthenticated(
                    factory,
                    AppRoles.Admin);

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
        }
    }

}
