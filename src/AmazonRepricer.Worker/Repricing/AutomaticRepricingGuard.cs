using Microsoft.Extensions.Options;

namespace AmazonRepricer.Worker.Repricing;

public sealed class AutomaticRepricingGuard : IAutomaticRepricingGuard
{
    private readonly WorkerOptions _options;

    public AutomaticRepricingGuard(IOptions<WorkerOptions> options)
    {
        _options = options.Value;
    }

    public RepricingGuardResult Evaluate(
        decimal currentPrice,
        decimal proposedPrice,
        DateTimeOffset nowUtc,
        DateTimeOffset? lastRepricedAtUtc)
    {
        if (currentPrice <= 0)
        {
            return RepricingGuardResult.Reject(
                "Current price must be greater than zero.");
        }

        if (proposedPrice <= 0)
        {
            return RepricingGuardResult.Reject(
                "Proposed price must be greater than zero.");
        }

        if (_options.MaxPriceChangePercentage <= 0 ||
            _options.MaxPriceChangePercentage > 100)
        {
            return RepricingGuardResult.Reject(
                "Max price change percentage configuration is invalid.");
        }

        if (_options.MinimumRepricingIntervalSeconds < 0)
        {
            return RepricingGuardResult.Reject(
                "Minimum repricing interval configuration is invalid.");
        }

        var percentageChange =
            Math.Abs(proposedPrice - currentPrice) /
            currentPrice *
            100m;

        if (percentageChange > _options.MaxPriceChangePercentage)
        {
            return RepricingGuardResult.Reject(
                $"Price change of {percentageChange:F2}% exceeds the configured " +
                $"maximum of {_options.MaxPriceChangePercentage:F2}%.");
        }

        if (lastRepricedAtUtc.HasValue)
        {
            var elapsed = nowUtc - lastRepricedAtUtc.Value;

            if (elapsed < TimeSpan.Zero)
            {
                return RepricingGuardResult.Reject(
                    "Last repricing timestamp is in the future.");
            }

            var minimumInterval =
                TimeSpan.FromSeconds(
                    _options.MinimumRepricingIntervalSeconds);

            if (elapsed < minimumInterval)
            {
                return RepricingGuardResult.Reject(
                    "Minimum repricing interval has not elapsed.");
            }
        }

        return RepricingGuardResult.Allow();
    }
}
