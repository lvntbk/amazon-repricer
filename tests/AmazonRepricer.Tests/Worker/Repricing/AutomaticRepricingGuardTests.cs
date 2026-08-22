using AmazonRepricer.Worker;
using AmazonRepricer.Worker.Repricing;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Tests.Worker.Repricing;

public sealed class AutomaticRepricingGuardTests
{
    private static AutomaticRepricingGuard CreateGuard(
        decimal maxChangePercentage = 10m,
        int minimumIntervalSeconds = 300)
    {
        var options = Options.Create(new WorkerOptions
        {
            MaxPriceChangePercentage = maxChangePercentage,
            MinimumRepricingIntervalSeconds = minimumIntervalSeconds
        });

        return new AutomaticRepricingGuard(options);
    }

    [Fact]
    public void ShouldAllowSafePriceChange()
    {
        var guard = CreateGuard();

        var result = guard.Evaluate(
            currentPrice: 1000m,
            proposedPrice: 950m,
            nowUtc: DateTimeOffset.UtcNow,
            lastRepricedAtUtc: null);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ShouldRejectPriceChangeAboveConfiguredPercentage()
    {
        var guard = CreateGuard(maxChangePercentage: 10m);

        var result = guard.Evaluate(
            currentPrice: 1000m,
            proposedPrice: 850m,
            nowUtc: DateTimeOffset.UtcNow,
            lastRepricedAtUtc: null);

        Assert.False(result.IsAllowed);
        Assert.Contains("exceeds", result.Reason);
    }

    [Fact]
    public void ShouldAllowPriceChangeExactlyAtConfiguredPercentage()
    {
        var guard = CreateGuard(maxChangePercentage: 10m);

        var result = guard.Evaluate(
            currentPrice: 1000m,
            proposedPrice: 900m,
            nowUtc: DateTimeOffset.UtcNow,
            lastRepricedAtUtc: null);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ShouldRejectNonPositiveProposedPrice()
    {
        var guard = CreateGuard();

        var result = guard.Evaluate(
            currentPrice: 1000m,
            proposedPrice: 0m,
            nowUtc: DateTimeOffset.UtcNow,
            lastRepricedAtUtc: null);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void ShouldRejectWhenCooldownHasNotElapsed()
    {
        var guard = CreateGuard(minimumIntervalSeconds: 300);

        var now = DateTimeOffset.UtcNow;

        var result = guard.Evaluate(
            currentPrice: 1000m,
            proposedPrice: 950m,
            nowUtc: now,
            lastRepricedAtUtc: now.AddSeconds(-60));

        Assert.False(result.IsAllowed);
        Assert.Contains("interval", result.Reason);
    }

    [Fact]
    public void ShouldAllowWhenCooldownHasElapsed()
    {
        var guard = CreateGuard(minimumIntervalSeconds: 300);

        var now = DateTimeOffset.UtcNow;

        var result = guard.Evaluate(
            currentPrice: 1000m,
            proposedPrice: 950m,
            nowUtc: now,
            lastRepricedAtUtc: now.AddSeconds(-300));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ShouldRejectFutureLastRepricingTimestamp()
    {
        var guard = CreateGuard();

        var now = DateTimeOffset.UtcNow;

        var result = guard.Evaluate(
            currentPrice: 1000m,
            proposedPrice: 950m,
            nowUtc: now,
            lastRepricedAtUtc: now.AddMinutes(1));

        Assert.False(result.IsAllowed);
    }
}
