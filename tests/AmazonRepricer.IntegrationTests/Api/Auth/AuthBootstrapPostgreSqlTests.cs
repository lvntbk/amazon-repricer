using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class AuthBootstrapPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public AuthBootstrapPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Startup_WithBootstrapEnabledAndNoAdmin_CreatesRequiredRolesAndInitialAdmin()
    {
        const string email =
            "initial-admin@example.test";

        const string password =
            "Initial-Admin-2026!";

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString,
                ("AuthBootstrap__Enabled", "true"),
                ("AuthBootstrap__AdminEmail", email),
                ("AuthBootstrap__AdminPassword", password));

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

            using var factory =
                AuthTestFactory.Create();

            using var client =
                AuthTestClientFactory.Create(factory);

            using var scope =
                factory.Services.CreateScope();

            var roleManager =
                scope.ServiceProvider
                    .GetRequiredService<
                        RoleManager<IdentityRole<Guid>>>();

            Assert.True(
                await roleManager.RoleExistsAsync(
                    AppRoles.Admin));

            Assert.True(
                await roleManager.RoleExistsAsync(
                    AppRoles.Operator));

            var userManager =
                scope.ServiceProvider
                    .GetRequiredService<
                        UserManager<AppUser>>();

            var admin =
                await userManager.FindByEmailAsync(
                    email);

            Assert.NotNull(admin);
            Assert.True(admin.IsActive);

            Assert.True(
                await userManager.CheckPasswordAsync(
                    admin,
                    password));

            var roles =
                await userManager.GetRolesAsync(
                    admin);

            Assert.Equal(
                [AppRoles.Admin],
                roles);

            var admins =
                await userManager.GetUsersInRoleAsync(
                    AppRoles.Admin);

            Assert.Single(
                admins,
                x => x.IsActive);
    }

    [Fact]
    public async Task Startup_WithBootstrapDisabled_CreatesRequiredRolesButNoUser()
    {
        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString,
                ("AuthBootstrap__Enabled", "false"),
                ("AuthBootstrap__AdminEmail", null),
                ("AuthBootstrap__AdminPassword", null));

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

            using var factory =
                AuthTestFactory.Create();

            using var client =
                AuthTestClientFactory.Create(factory);

            using var scope =
                factory.Services.CreateScope();

            var roleManager =
                scope.ServiceProvider
                    .GetRequiredService<
                        RoleManager<IdentityRole<Guid>>>();

            Assert.True(
                await roleManager.RoleExistsAsync(
                    AppRoles.Admin));

            Assert.True(
                await roleManager.RoleExistsAsync(
                    AppRoles.Operator));

            var userManager =
                scope.ServiceProvider
                    .GetRequiredService<
                        UserManager<AppUser>>();

            Assert.False(
                await userManager.Users.AnyAsync());
        }

    [Theory]
    [InlineData(null, "Initial-Admin-2026!")]
    [InlineData("initial-admin@example.test", null)]
    [InlineData("", "Initial-Admin-2026!")]
    [InlineData("initial-admin@example.test", "")]
    public async Task Startup_WithBootstrapEnabledButMissingCredentials_FailsClosed(
        string? email,
        string? password)
    {
        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString,
                ("AuthBootstrap__Enabled", "true"),
                ("AuthBootstrap__AdminEmail", email),
                ("AuthBootstrap__AdminPassword", password));

            using var factory =
                AuthTestFactory.Create();

            var exception =
                await Assert.ThrowsAnyAsync<Exception>(
                    async () =>
                    {
                        using var client =
                            AuthTestClientFactory.Create(factory);

                        await client.GetAsync("/");
                    });

            Assert.Contains(
                "Auth bootstrap is enabled",
                exception.ToString(),
                StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_WithExistingAdmin_DoesNotCreateSecondAdminOrChangeExistingCredentials()
    {
        const string existingEmail =
            "existing-admin@example.test";

        const string existingPassword =
            "Existing-Admin-2026!";

        const string bootstrapEmail =
            "bootstrap-admin@example.test";

        const string bootstrapPassword =
            "Bootstrap-Admin-2026!";

        var existingAdminId =
            Guid.NewGuid();

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString,
                ("AuthBootstrap__Enabled", "true"),
                ("AuthBootstrap__AdminEmail", bootstrapEmail),
                ("AuthBootstrap__AdminPassword", bootstrapPassword));

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

            using (var seedFactory =
                AuthTestFactory.Create())
            {
                using var seedClient =
                    AuthTestClientFactory.Create(seedFactory);

                using var scope =
                    seedFactory.Services.CreateScope();

                var userManager =
                    scope.ServiceProvider
                        .GetRequiredService<
                            UserManager<AppUser>>();

                var bootstrapCreatedAdmin =
                    await userManager.FindByEmailAsync(
                        bootstrapEmail);

                Assert.NotNull(bootstrapCreatedAdmin);

                await userManager.DeleteAsync(
                    bootstrapCreatedAdmin);

                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    existingEmail,
                    existingPassword,
                    AppRoles.Admin,
                    isActive: true,
                    userId: existingAdminId);
            }

            using var factory =
                AuthTestFactory.Create();

            using var client =
                AuthTestClientFactory.Create(factory);

            using var verificationScope =
                factory.Services.CreateScope();

            var verificationUserManager =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        UserManager<AppUser>>();

            var existingAdmin =
                await verificationUserManager.FindByIdAsync(
                    existingAdminId.ToString());

            Assert.NotNull(existingAdmin);

            Assert.True(
                await verificationUserManager.CheckPasswordAsync(
                    existingAdmin,
                    existingPassword));

            Assert.False(
                await verificationUserManager.CheckPasswordAsync(
                    existingAdmin,
                    bootstrapPassword));

            var bootstrapUser =
                await verificationUserManager.FindByEmailAsync(
                    bootstrapEmail);

            Assert.Null(bootstrapUser);

            var admins =
                await verificationUserManager.GetUsersInRoleAsync(
                    AppRoles.Admin);

            Assert.Single(
                admins,
                x => x.IsActive);

            Assert.Equal(
                existingAdminId,
                admins.Single(x => x.IsActive).Id);
    }

    [Fact]
    public async Task Startup_WithConcurrentBootstrap_CreatesSingleInitialAdmin()
    {
        const string email =
            "concurrent-initial-admin@example.test";

        const string password =
            "Concurrent-Initial-Admin-2026!";

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString,
                ("AuthBootstrap__Enabled", "true"),
                ("AuthBootstrap__AdminEmail", email),
                ("AuthBootstrap__AdminPassword", password));

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

            using var factory1 =
                AuthTestFactory.Create();

            using var factory2 =
                AuthTestFactory.Create();

            var task1 =
                Task.Run(
                    () =>
                        AuthTestClientFactory.Create(factory1));

            var task2 =
                Task.Run(
                    () =>
                        AuthTestClientFactory.Create(factory2));

            var clients =
                await Task.WhenAll(
                    task1,
                    task2);

            using var client1 =
                clients[0];

            using var client2 =
                clients[1];

            using var scope =
                factory1.Services.CreateScope();

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

            var admin =
                admins.Single(
                    x => x.IsActive);

            Assert.Equal(
                email,
                admin.Email);

            Assert.True(
                await userManager.CheckPasswordAsync(
                    admin,
                    password));

            var usersWithBootstrapEmail =
                await userManager.Users
                    .Where(x => x.Email == email)
                    .ToListAsync();

            Assert.Single(
                usersWithBootstrapEmail);
    }

    [Fact]
    public async Task Startup_WithOnlyInactiveAdmin_FailsClosedInsteadOfCreatingBootstrapAdmin()
    {
        const string inactiveEmail =
            "inactive-admin@example.test";

        const string inactivePassword =
            "Inactive-Admin-2026!";

        const string bootstrapEmail =
            "replacement-bootstrap-admin@example.test";

        const string bootstrapPassword =
            "Bootstrap-Admin-2026!";

        using var environment =
            AuthTestEnvironmentScope.CreateDefault(
                _database.ConnectionString,
                ("Jwt__AccessTokenLifetimeMinutes", "15"),
                ("Jwt__RefreshTokenLifetimeDays", "30"),
                ("AuthBootstrap__Enabled", "false"),
                ("AuthBootstrap__AdminEmail", bootstrapEmail),
                ("AuthBootstrap__AdminPassword", bootstrapPassword));

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

            using (var seedFactory =
                AuthTestFactory.Create())
            {
                using var scope =
                    seedFactory.Services.CreateScope();

                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    inactiveEmail,
                    inactivePassword,
                    AppRoles.Admin,
                    isActive: false);
            }

            environment.Set(
                "AuthBootstrap__Enabled",
                "true");

            using var factory =
                AuthTestFactory.Create();

            var exception =
                await Assert.ThrowsAnyAsync<Exception>(
                    async () =>
                    {
                        using var client =
                            AuthTestClientFactory.Create(factory);

                        await client.GetAsync("/");
                    });

            Assert.Contains(
                "active Admin",
                exception.ToString(),
                StringComparison.OrdinalIgnoreCase);

            await using var verificationContext =
                _database.CreateAuthDbContext();

            Assert.False(
                await verificationContext.Users.AnyAsync(
                    x => x.Email == bootstrapEmail));
        }
        finally
        {
            await using (var cleanupContext =
                _database.CreateAuthDbContext())
            {
                await cleanupContext.RefreshTokens
                    .ExecuteDeleteAsync();

                await cleanupContext.UserTokens
                    .ExecuteDeleteAsync();

                await cleanupContext.UserLogins
                    .ExecuteDeleteAsync();

                await cleanupContext.UserClaims
                    .ExecuteDeleteAsync();

                await cleanupContext.UserRoles
                    .ExecuteDeleteAsync();

                await cleanupContext.RoleClaims
                    .ExecuteDeleteAsync();

                await cleanupContext.Users
                    .ExecuteDeleteAsync();

                await cleanupContext.Roles
                    .ExecuteDeleteAsync();
            }

        }
    }

}
