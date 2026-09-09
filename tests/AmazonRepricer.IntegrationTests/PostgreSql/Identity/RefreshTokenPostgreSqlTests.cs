using AmazonRepricer.Infrastructure.Identity;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.IntegrationTests.PostgreSql.Identity;

[Collection(PostgreSqlCollection.Name)]
public sealed class RefreshTokenPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public RefreshTokenPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task RefreshTokenTable_Exists()
    {
        await using var dbContext =
            _database.CreateAuthDbContext();

        await dbContext.Database.OpenConnectionAsync();

        await using var command =
            dbContext.Database
                .GetDbConnection()
                .CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name = 'AuthRefreshTokens';
            """;

        var result =
            await command.ExecuteScalarAsync();

        Assert.Equal(
            1L,
            Convert.ToInt64(result));
    }

    [Fact]
    public async Task DuplicateTokenHash_IsRejectedByDatabase()
    {
        var user =
            await CreateUserAsync();

        var now =
            DateTime.UtcNow;

        var tokenHash =
            new string('a', 64);

        await using (var firstContext =
            _database.CreateAuthDbContext())
        {
            firstContext.RefreshTokens.Add(
                new AuthRefreshToken
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    FamilyId = Guid.NewGuid(),
                    TokenHash = tokenHash,
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddDays(7)
                });

            await firstContext.SaveChangesAsync();
        }

        await using var secondContext =
            _database.CreateAuthDbContext();

        secondContext.RefreshTokens.Add(
            new AuthRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FamilyId = Guid.NewGuid(),
                TokenHash = tokenHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(7)
            });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ExpiryNotAfterCreation_IsRejectedByDatabase()
    {
        var user =
            await CreateUserAsync();

        var now =
            DateTime.UtcNow;

        await using var dbContext =
            _database.CreateAuthDbContext();

        dbContext.RefreshTokens.Add(
            new AuthRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FamilyId = Guid.NewGuid(),
                TokenHash =
                    Guid.NewGuid().ToString("N") +
                    Guid.NewGuid().ToString("N"),
                CreatedAtUtc = now,
                ExpiresAtUtc = now
            });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task UnknownUserId_IsRejectedByDatabase()
    {
        var now =
            DateTime.UtcNow;

        await using var dbContext =
            _database.CreateAuthDbContext();

        dbContext.RefreshTokens.Add(
            new AuthRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                FamilyId = Guid.NewGuid(),
                TokenHash =
                    Guid.NewGuid().ToString("N") +
                    Guid.NewGuid().ToString("N"),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(7)
            });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ReplacementWithoutRevocation_IsRejectedByDatabase()
    {
        var user =
            await CreateUserAsync();

        var now =
            DateTime.UtcNow;

        var familyId =
            Guid.NewGuid();

        var replacementId =
            Guid.NewGuid();

        await using (var replacementContext =
            _database.CreateAuthDbContext())
        {
            replacementContext.RefreshTokens.Add(
                new AuthRefreshToken
                {
                    Id = replacementId,
                    UserId = user.Id,
                    FamilyId = familyId,
                    TokenHash =
                        Guid.NewGuid().ToString("N") +
                        Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddDays(7)
                });

            await replacementContext.SaveChangesAsync();
        }

        await using var dbContext =
            _database.CreateAuthDbContext();

        dbContext.RefreshTokens.Add(
            new AuthRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FamilyId = familyId,
                TokenHash =
                    Guid.NewGuid().ToString("N") +
                    Guid.NewGuid().ToString("N"),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(7),
                ReplacedByTokenId = replacementId,
                RevokedAtUtc = null
            });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task RefreshTokenTable_DoesNotContainPlaintextTokenColumn()
    {
        await using var dbContext =
            _database.CreateAuthDbContext();

        await dbContext.Database.OpenConnectionAsync();

        await using var command =
            dbContext.Database
                .GetDbConnection()
                .CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'AuthRefreshTokens'
              AND column_name IN (
                  'Token',
                  'RefreshToken',
                  'PlaintextToken',
                  'TokenValue'
              );
            """;

        var result =
            await command.ExecuteScalarAsync();

        Assert.Equal(
            0L,
            Convert.ToInt64(result));
    }

    [Fact]
    public async Task SelfReplacement_IsRejectedByDatabase()
    {
        var user =
            await CreateUserAsync();

        var now =
            DateTime.UtcNow;

        var tokenId =
            Guid.NewGuid();

        await using var dbContext =
            _database.CreateAuthDbContext();

        dbContext.RefreshTokens.Add(
            new AuthRefreshToken
            {
                Id = tokenId,
                UserId = user.Id,
                FamilyId = Guid.NewGuid(),
                TokenHash =
                    Guid.NewGuid().ToString("N") +
                    Guid.NewGuid().ToString("N"),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(7),
                RevokedAtUtc = now,
                ReplacedByTokenId = tokenId
            });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task RevocationBeforeCreation_IsRejectedByDatabase()
    {
        var user =
            await CreateUserAsync();

        var now =
            DateTime.UtcNow;

        await using var dbContext =
            _database.CreateAuthDbContext();

        dbContext.RefreshTokens.Add(
            new AuthRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FamilyId = Guid.NewGuid(),
                TokenHash =
                    Guid.NewGuid().ToString("N") +
                    Guid.NewGuid().ToString("N"),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(7),
                RevokedAtUtc = now.AddMinutes(-1)
            });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync());
    }


    private async Task<AppUser> CreateUserAsync()
    {
        var email =
            $"refresh-token-{Guid.NewGuid():N}@example.test";

        var user =
            new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                NormalizedUserName =
                    email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail =
                    email.ToUpperInvariant(),
                EmailConfirmed = true,
                IsActive = true,
                LockoutEnabled = true,
                CreatedAtUtc =
                    DateTime.UtcNow,
                SecurityStamp =
                    Guid.NewGuid().ToString("N"),
                ConcurrencyStamp =
                    Guid.NewGuid().ToString("N")
            };

        await using var dbContext =
            _database.CreateAuthDbContext();

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        return user;
    }
}
