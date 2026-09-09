using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Infrastructure.Pricing;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.IntegrationTests.Infrastructure.Pricing;

[Collection(PostgreSqlCollection.Name)]
public sealed class DbPriceUpdateSafetyGatePostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public DbPriceUpdateSafetyGatePostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsConfiguredMaximumPriceChangePercentage()
    {
        await using var dbContext =
            _database.CreateDbContext();

        var settings =
            await dbContext.RepricingSafetySettings
                .SingleAsync(
                    x => x.Id == RepricingSafetySettings.GlobalId);

        var originalPriceUpdatesEnabled =
            settings.PriceUpdatesEnabled;

        var originalMaxPriceChangePercentage =
            settings.MaxPriceChangePercentage;

        var originalUpdatedAtUtc =
            settings.UpdatedAtUtc;

        try
        {
            settings.PriceUpdatesEnabled = true;
            settings.MaxPriceChangePercentage = 7.5m;
            settings.UpdatedAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync();

            var gate =
                new DbPriceUpdateSafetyGate(dbContext);

            var result =
                await gate.EvaluateAsync();

            Assert.True(result.IsAllowed);
            Assert.Equal(
                7.5m,
                result.MaxPriceChangePercentage);
        }
        finally
        {
            settings.PriceUpdatesEnabled =
                originalPriceUpdatesEnabled;

            settings.MaxPriceChangePercentage =
                originalMaxPriceChangePercentage;

            settings.UpdatedAtUtc =
                originalUpdatedAtUtc;

            await dbContext.SaveChangesAsync();
        }
    }
}
