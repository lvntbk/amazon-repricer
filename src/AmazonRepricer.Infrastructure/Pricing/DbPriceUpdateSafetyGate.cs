using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Infrastructure.Pricing;

public sealed class DbPriceUpdateSafetyGate
    : IPriceUpdateSafetyGate
{
    private readonly RepricerDbContext _dbContext;

    public DbPriceUpdateSafetyGate(
        RepricerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PriceUpdateSafetyGateResult> EvaluateAsync(
        CancellationToken cancellationToken = default)
    {
        var settings =
            await _dbContext.RepricingSafetySettings
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == RepricingSafetySettings.GlobalId,
                    cancellationToken);

        if (settings is null)
        {
            return new PriceUpdateSafetyGateResult(
                false,
                "Global repricing safety settings are missing.");
        }

        if (!settings.PriceUpdatesEnabled)
        {
            return new PriceUpdateSafetyGateResult(
                false,
                "Global price updates are disabled.");
        }

        return new PriceUpdateSafetyGateResult(
            true,
            "Global price updates are enabled.");
    }
}
