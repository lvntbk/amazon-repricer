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
                ["AuthBootstrap__Enabled"] =
                    "true",
                ["AuthBootstrap__AdminEmail"] =
                    email,
                ["AuthBootstrap__AdminPassword"] =
                    password
            };

        var originals =
            variables.Keys.ToDictionary(
                key => key,
                Environment.GetEnvironmentVariable);

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
                Environment.SetEnvironmentVariable(
                    variable.Key,
                    variable.Value);
            }

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
        finally
        {
            foreach (var original in originals)
            {
                Environment.SetEnvironmentVariable(
                    original.Key,
                    original.Value);
            }
        }
    }

    [Fact]
    public async Task Startup_WithBootstrapDisabled_CreatesRequiredRolesButNoUser()
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
                ["AuthBootstrap__Enabled"] =
                    "false",
                ["AuthBootstrap__AdminEmail"] =
                    null,
                ["AuthBootstrap__AdminPassword"] =
                    null
            };

        var originals =
            variables.Keys.ToDictionary(
                key => key,
                Environment.GetEnvironmentVariable);

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
                Environment.SetEnvironmentVariable(
                    variable.Key,
                    variable.Value);
            }

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
        finally
        {
            foreach (var original in originals)
            {
                Environment.SetEnvironmentVariable(
                    original.Key,
                    original.Value);
            }
        }
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
                ["AuthBootstrap__Enabled"] =
                    "true",
                ["AuthBootstrap__AdminEmail"] =
                    email,
                ["AuthBootstrap__AdminPassword"] =
                    password
            };

        var originals =
            variables.Keys.ToDictionary(
                key => key,
                Environment.GetEnvironmentVariable);

        try
        {
            foreach (var variable in variables)
            {
                Environment.SetEnvironmentVariable(
                    variable.Key,
                    variable.Value);
            }

            using var factory =
                new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(
                        builder =>
                            builder.UseEnvironment("Testing"));

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
                "Auth bootstrap is enabled",
                exception.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            foreach (var original in originals)
            {
                Environment.SetEnvironmentVariable(
                    original.Key,
                    original.Value);
            }
        }
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
                ["AuthBootstrap__Enabled"] =
                    "true",
                ["AuthBootstrap__AdminEmail"] =
                    bootstrapEmail,
                ["AuthBootstrap__AdminPassword"] =
                    bootstrapPassword
            };

        var originals =
            variables.Keys.ToDictionary(
                key => key,
                Environment.GetEnvironmentVariable);

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
                Environment.SetEnvironmentVariable(
                    variable.Key,
                    variable.Value);
            }

            using (var seedFactory =
                new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(
                        builder =>
                            builder.UseEnvironment("Testing")))
            {
                using var seedClient =
                    seedFactory.CreateClient(
                        new WebApplicationFactoryClientOptions
                        {
                            AllowAutoRedirect = false
                        });

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

                var replacementAdmin =
                    new AppUser
                    {
                        Id = existingAdminId,
                        UserName = existingEmail,
                        Email = existingEmail,
                        EmailConfirmed = true,
                        IsActive = true
                    };

                var createResult =
                    await userManager.CreateAsync(
                        replacementAdmin,
                        existingPassword);

                Assert.True(
                    createResult.Succeeded,
                    string.Join(
                        "; ",
                        createResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));

                var roleResult =
                    await userManager.AddToRoleAsync(
                        replacementAdmin,
                        AppRoles.Admin);

                Assert.True(
                    roleResult.Succeeded,
                    string.Join(
                        "; ",
                        roleResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));
            }

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
        finally
        {
            foreach (var original in originals)
            {
                Environment.SetEnvironmentVariable(
                    original.Key,
                    original.Value);
            }
        }
    }

    [Fact]
    public async Task Startup_WithConcurrentBootstrap_CreatesSingleInitialAdmin()
    {
        const string email =
            "concurrent-initial-admin@example.test";

        const string password =
            "Concurrent-Initial-Admin-2026!";

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
                ["AuthBootstrap__Enabled"] =
                    "true",
                ["AuthBootstrap__AdminEmail"] =
                    email,
                ["AuthBootstrap__AdminPassword"] =
                    password
            };

        var originals =
            variables.Keys.ToDictionary(
                key => key,
                Environment.GetEnvironmentVariable);

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
                Environment.SetEnvironmentVariable(
                    variable.Key,
                    variable.Value);
            }

            using var factory1 =
                new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(
                        builder =>
                            builder.UseEnvironment("Testing"));

            using var factory2 =
                new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(
                        builder =>
                            builder.UseEnvironment("Testing"));

            var task1 =
                Task.Run(
                    () =>
                        factory1.CreateClient(
                            new WebApplicationFactoryClientOptions
                            {
                                AllowAutoRedirect = false
                            }));

            var task2 =
                Task.Run(
                    () =>
                        factory2.CreateClient(
                            new WebApplicationFactoryClientOptions
                            {
                                AllowAutoRedirect = false
                            }));

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
        finally
        {
            foreach (var original in originals)
            {
                Environment.SetEnvironmentVariable(
                    original.Key,
                    original.Value);
            }
        }
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
                    "false",
                ["AuthBootstrap__AdminEmail"] =
                    bootstrapEmail,
                ["AuthBootstrap__AdminPassword"] =
                    bootstrapPassword
            };

        var originals =
            variables.Keys.ToDictionary(
                key => key,
                Environment.GetEnvironmentVariable);

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
                Environment.SetEnvironmentVariable(
                    variable.Key,
                    variable.Value);
            }

            using (var seedFactory =
                new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(
                        builder =>
                            builder.UseEnvironment("Testing")))
            {
                using var scope =
                    seedFactory.Services.CreateScope();

                var userManager =
                    scope.ServiceProvider
                        .GetRequiredService<UserManager<AppUser>>();

                var inactiveAdmin =
                    new AppUser
                    {
                        Id = Guid.NewGuid(),
                        UserName = inactiveEmail,
                        Email = inactiveEmail,
                        EmailConfirmed = true,
                        IsActive = false
                    };

                var createResult =
                    await userManager.CreateAsync(
                        inactiveAdmin,
                        inactivePassword);

                Assert.True(
                    createResult.Succeeded,
                    string.Join(
                        "; ",
                        createResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));

                var roleResult =
                    await userManager.AddToRoleAsync(
                        inactiveAdmin,
                        AppRoles.Admin);

                Assert.True(
                    roleResult.Succeeded,
                    string.Join(
                        "; ",
                        roleResult.Errors.Select(
                            x =>
                                $"{x.Code}: {x.Description}")));
            }

            Environment.SetEnvironmentVariable(
                "AuthBootstrap__Enabled",
                "true");

            using var factory =
                new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(
                        builder =>
                            builder.UseEnvironment("Testing"));

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

            foreach (var original in originals)
            {
                Environment.SetEnvironmentVariable(
                    original.Key,
                    original.Value);
            }
        }
    }

}
