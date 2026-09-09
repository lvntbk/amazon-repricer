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
        if (_options.MinimumRepricingIntervalSeconds < 0)
        {
            return RepricingGuardResult.Reject(
                "Minimum repricing interval configuration is invalid.");
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
