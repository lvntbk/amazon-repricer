using AmazonRepricer.Infrastructure;
using AmazonRepricer.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.IntegrationTests.PostgreSql.Identity;

[Collection(PostgreSqlCollection.Name)]
public sealed class IdentityPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public IdentityPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task CreateUser_WithStrongPassword_StoresOnlyPasswordHash()
    {
        var suffix = Guid.NewGuid().ToString("N");

        var email =
            $"password-{suffix}@example.test";

        const string password =
            "Correct-Horse-2026!";

        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] =
                            _database.ConnectionString
                    })
                .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddInfrastructure(configuration);

        await using var serviceProvider =
            services.BuildServiceProvider();

        await using var scope =
            serviceProvider.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true
        };

        try
        {
            var result =
                await userManager.CreateAsync(
                    user,
                    password);

            Assert.True(
                result.Succeeded,
                string.Join(
                    "; ",
                    result.Errors.Select(
                        x => $"{x.Code}: {x.Description}")));

            await using var verificationContext =
                _database.CreateAuthDbContext();

            var persistedUser =
                await verificationContext.Users
                    .SingleAsync(x => x.Id == user.Id);

            Assert.False(
                string.IsNullOrWhiteSpace(
                    persistedUser.PasswordHash));

            Assert.NotEqual(
                password,
                persistedUser.PasswordHash);

            Assert.DoesNotContain(
                password,
                persistedUser.PasswordHash!,
                StringComparison.Ordinal);

            Assert.True(
                await userManager.CheckPasswordAsync(
                    user,
                    password));
        }
        finally
        {
            await using var cleanup =
                _database.CreateAuthDbContext();

            var persistedUser =
                await cleanup.Users.FindAsync(user.Id);

            if (persistedUser is not null)
            {
                cleanup.Users.Remove(persistedUser);
                await cleanup.SaveChangesAsync();
            }
        }
    }


    [Fact]
    public async Task CreateUser_WithShortPassword_IsRejectedAndNotPersisted()
    {
        var suffix = Guid.NewGuid().ToString("N");

        var email =
            $"short-password-{suffix}@example.test";

        const string password =
            "Abcdefghijk";

        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] =
                            _database.ConnectionString
                    })
                .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddInfrastructure(configuration);

        await using var serviceProvider =
            services.BuildServiceProvider();

        await using var scope =
            serviceProvider.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true
        };

        var result =
            await userManager.CreateAsync(
                user,
                password);

        Assert.False(result.Succeeded);

        Assert.Contains(
            result.Errors,
            error =>
                error.Code == "PasswordTooShort");

        await using var verificationContext =
            _database.CreateAuthDbContext();

        var persistedUser =
            await verificationContext.Users
                .SingleOrDefaultAsync(
                    x => x.Id == user.Id);

        Assert.Null(persistedUser);
    }


    [Fact]
    public async Task FourFailedAccessAttempts_LockUserForTenMinutes()
    {
        var suffix = Guid.NewGuid().ToString("N");

        var email =
            $"lockout-{suffix}@example.test";

        const string password =
            "Correct-Horse-2026!";

        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] =
                            _database.ConnectionString
                    })
                .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddInfrastructure(configuration);

        await using var serviceProvider =
            services.BuildServiceProvider();

        await using var scope =
            serviceProvider.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true
        };

        try
        {
            var createResult =
                await userManager.CreateAsync(
                    user,
                    password);

            Assert.True(
                createResult.Succeeded,
                string.Join(
                    "; ",
                    createResult.Errors.Select(
                        x => $"{x.Code}: {x.Description}")));

            Assert.True(
                await userManager.GetLockoutEnabledAsync(user));

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var failedResult =
                    await userManager.AccessFailedAsync(user);

                Assert.True(failedResult.Succeeded);

                Assert.False(
                    await userManager.IsLockedOutAsync(user));
            }

            var beforeFourthFailure =
                DateTimeOffset.UtcNow;

            var fourthFailure =
                await userManager.AccessFailedAsync(user);

            Assert.True(fourthFailure.Succeeded);

            Assert.True(
                await userManager.IsLockedOutAsync(user));

            var lockoutEnd =
                await userManager.GetLockoutEndDateAsync(user);

            Assert.NotNull(lockoutEnd);

            Assert.InRange(
                lockoutEnd!.Value,
                beforeFourthFailure.AddMinutes(9),
                beforeFourthFailure.AddMinutes(11));
        }
        finally
        {
            await using var cleanup =
                _database.CreateAuthDbContext();

            var persistedUser =
                await cleanup.Users.FindAsync(user.Id);

            if (persistedUser is not null)
            {
                cleanup.Users.Remove(persistedUser);
                await cleanup.SaveChangesAsync();
            }
        }
    }


    [Fact]
    public async Task DuplicateNormalizedEmail_IsRejectedByDatabase()
    {
        var suffix = Guid.NewGuid().ToString("N");

        var email =
            $"identity-{suffix}@example.test";

        var normalizedEmail =
            email.ToUpperInvariant();

        var firstUser = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = normalizedEmail,
            Email = email,
            NormalizedEmail = normalizedEmail,
            EmailConfirmed = true,
            IsActive = true
        };

        await using (var firstContext =
            _database.CreateAuthDbContext())
        {
            firstContext.Users.Add(firstUser);
            await firstContext.SaveChangesAsync();
        }

        try
        {
            var secondUser = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName =
                    $"second-{suffix}@example.test",
                NormalizedUserName =
                    $"SECOND-{suffix}@EXAMPLE.TEST",
                Email = email,
                NormalizedEmail = normalizedEmail,
                EmailConfirmed = true,
                IsActive = true
            };

            await using var secondContext =
                _database.CreateAuthDbContext();

            secondContext.Users.Add(secondUser);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => secondContext.SaveChangesAsync());
        }
        finally
        {
            await using var cleanup =
                _database.CreateAuthDbContext();

            var persistedUser =
                await cleanup.Users.FindAsync(firstUser.Id);

            if (persistedUser is not null)
            {
                cleanup.Users.Remove(persistedUser);
                await cleanup.SaveChangesAsync();
            }
        }
    }
}
