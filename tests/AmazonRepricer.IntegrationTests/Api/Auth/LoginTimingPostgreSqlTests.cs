using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using AmazonRepricer.Infrastructure.Identity;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class LoginTimingPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public LoginTimingPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Login_WithUnknownUser_PerformsPasswordHashVerification()
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

            var counter =
                new PasswordVerificationCounter();

            using var factory =
                AuthTestFactory.Create(
                    builder =>
                    {
                        builder.ConfigureTestServices(
                                services =>
                                {
                                    services.RemoveAll<
                                        IPasswordHasher<AppUser>>();

                                    services.AddSingleton(counter);

                                    services.AddScoped<
                                        IPasswordHasher<AppUser>,
                                        CountingPasswordHasher>();
                                });
                        });

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
                        Email =
                            $"unknown-{Guid.NewGuid():N}@example.test",
                        Password =
                            "Wrong-Horse-2026!"
                    });

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);

            Assert.Equal(
                1,
                counter.VerifyCount);
        }
        finally
        {



        }
    }

    [Fact]
    public async Task Login_WithInactiveUser_PerformsDummyPasswordHashVerification()
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

        var userId =
            Guid.NewGuid();

        var email =
            $"timing-inactive-{Guid.NewGuid():N}@example.test";

        using var environment =
            AuthTestEnvironmentScope.Capture(
                connectionStringVariable,
                issuerVariable,
                audienceVariable,
                signingKeyVariable);

try
        {
            await using (var dbContext =
                _database.CreateAuthDbContext())
            {
                var user =
                    new AppUser
                    {
                        Id = userId,
                        UserName = email,
                        NormalizedUserName =
                            email.ToUpperInvariant(),
                        Email = email,
                        NormalizedEmail =
                            email.ToUpperInvariant(),
                        EmailConfirmed = true,
                        IsActive = false,
                        LockoutEnabled = true,
                        SecurityStamp =
                            Guid.NewGuid().ToString("N"),
                        ConcurrencyStamp =
                            Guid.NewGuid().ToString("N"),
                        CreatedAtUtc =
                            DateTime.UtcNow
                    };

                var passwordHasher =
                    new PasswordHasher<AppUser>();

                user.PasswordHash =
                    passwordHasher.HashPassword(
                        user,
                        password);

                dbContext.Users.Add(user);

                await dbContext.SaveChangesAsync();
            }

            environment.Set(connectionStringVariable, _database.ConnectionString);

            environment.Set(issuerVariable, "AmazonRepricer.IntegrationTests");

            environment.Set(audienceVariable, "AmazonRepricer.IntegrationTests");

            environment.Set(signingKeyVariable, "integration-test-signing-key-32-bytes-minimum");

            var counter =
                new PasswordVerificationCounter();

            using var factory =
                AuthTestFactory.Create(
                    builder =>
                    {
                        builder.ConfigureTestServices(
                                services =>
                                {
                                    services.RemoveAll<
                                        IPasswordHasher<AppUser>>();

                                    services.AddSingleton(counter);

                                    services.AddScoped<
                                        IPasswordHasher<AppUser>,
                                        CountingPasswordHasher>();
                                });
                        });

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

            Assert.Equal(
                1,
                counter.VerifyCount);
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




        }
    }


    [Fact]
    public async Task Login_WithLockedUser_PerformsDummyPasswordHashVerification()
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

        var userId =
            Guid.NewGuid();

        var email =
            $"timing-locked-{Guid.NewGuid():N}@example.test";

        using var environment =
            AuthTestEnvironmentScope.Capture(
                connectionStringVariable,
                issuerVariable,
                audienceVariable,
                signingKeyVariable);

try
        {
            await using (var dbContext =
                _database.CreateAuthDbContext())
            {
                var user =
                    new AppUser
                    {
                        Id = userId,
                        UserName = email,
                        NormalizedUserName =
                            email.ToUpperInvariant(),
                        Email = email,
                        NormalizedEmail =
                            email.ToUpperInvariant(),
                        EmailConfirmed = true,
                        IsActive = true,
                        LockoutEnabled = true,
                        LockoutEnd =
                            DateTimeOffset.UtcNow.AddMinutes(5),
                        SecurityStamp =
                            Guid.NewGuid().ToString("N"),
                        ConcurrencyStamp =
                            Guid.NewGuid().ToString("N"),
                        CreatedAtUtc =
                            DateTime.UtcNow
                    };

                var passwordHasher =
                    new PasswordHasher<AppUser>();

                user.PasswordHash =
                    passwordHasher.HashPassword(
                        user,
                        password);

                dbContext.Users.Add(user);

                await dbContext.SaveChangesAsync();
            }

            environment.Set(connectionStringVariable, _database.ConnectionString);

            environment.Set(issuerVariable, "AmazonRepricer.IntegrationTests");

            environment.Set(audienceVariable, "AmazonRepricer.IntegrationTests");

            environment.Set(signingKeyVariable, "integration-test-signing-key-32-bytes-minimum");

            var counter =
                new PasswordVerificationCounter();

            using var factory =
                AuthTestFactory.Create(
                    builder =>
                    {
                        builder.ConfigureTestServices(
                                services =>
                                {
                                    services.RemoveAll<
                                        IPasswordHasher<AppUser>>();

                                    services.AddSingleton(counter);

                                    services.AddScoped<
                                        IPasswordHasher<AppUser>,
                                        CountingPasswordHasher>();
                                });
                        });

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

            Assert.Equal(
                1,
                counter.VerifyCount);
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




        }
    }


    private sealed class PasswordVerificationCounter
    {
        private int _verifyCount;

        public int VerifyCount =>
            Volatile.Read(ref _verifyCount);

        public void Increment()
        {
            Interlocked.Increment(
                ref _verifyCount);
        }
    }

    private sealed class CountingPasswordHasher
        : IPasswordHasher<AppUser>
    {
        private readonly PasswordVerificationCounter _counter;

        public CountingPasswordHasher(
            PasswordVerificationCounter counter)
        {
            _counter = counter;
        }

        public string HashPassword(
            AppUser user,
            string password)
        {
            return "unused";
        }

        public PasswordVerificationResult VerifyHashedPassword(
            AppUser user,
            string hashedPassword,
            string providedPassword)
        {
            _counter.Increment();

            return PasswordVerificationResult.Failed;
        }
    }
}
