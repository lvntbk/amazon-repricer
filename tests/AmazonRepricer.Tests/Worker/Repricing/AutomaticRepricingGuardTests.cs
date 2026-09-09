using AmazonRepricer.Worker;
using AmazonRepricer.Worker.Repricing;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Tests.Worker.Repricing;

public sealed class AutomaticRepricingGuardTests
{
    private static AutomaticRepricingGuard CreateGuard(
        int minimumIntervalSeconds = 300)
    {
        var options = Options.Create(new WorkerOptions
        {
            MinimumRepricingIntervalSeconds =
                minimumIntervalSeconds
        });

        return new AutomaticRepricingGuard(options);
    }

    [Fact]
    public void ShouldAllowWhenNoPreviousRepricingExists()
    {
        var guard = CreateGuard();

        var result = guard.Evaluate(
            currentPrice: 1000m,
            proposedPrice: 500m,
            nowUtc: DateTimeOffset.UtcNow,
            lastRepricedAtUtc: null);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ShouldRejectInvalidMinimumInterval()
    {
        var guard = CreateGuard(
            minimumIntervalSeconds: -1);

        var result = guard.Evaluate(
            currentPrice: 1000m,
            proposedPrice: 950m,
            nowUtc: DateTimeOffset.UtcNow,
            lastRepricedAtUtc: null);

        Assert.False(result.IsAllowed);
        Assert.Contains(
            "configuration",
            result.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldRejectWhenCooldownHasNotElapsed()
    {
        var guard = CreateGuard(
            minimumIntervalSeconds: 300);

        var now = DateTimeOffset.UtcNow;

        var result = guard.Evaluate(
            currentPrice: 1000m,
            proposedPrice: 950m,
            nowUtc: now,
            lastRepricedAtUtc:
                now.AddSeconds(-60));

        Assert.False(result.IsAllowed);
        Assert.Contains(
            "interval",
            result.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldAllowWhenCooldownHasElapsed()
    {
        var guard = CreateGuard(
            minimumIntervalSeconds: 300);

        var now = DateTimeOffset.UtcNow;

        var result = guard.Evaluate(
            currentPrice: 1000m,
            proposedPrice: 950m,
            nowUtc: now,
            lastRepricedAtUtc:
                now.AddSeconds(-300));

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
            lastRepricedAtUtc:
                now.AddMinutes(1));

        Assert.False(result.IsAllowed);
        Assert.Contains(
            "future",
            result.Reason,
            StringComparison.OrdinalIgnoreCase);
    }
}
