namespace AmazonRepricer.Worker.Repricing;

public interface IAutomaticRepricingGuard
{
    RepricingGuardResult Evaluate(
        decimal currentPrice,
        decimal proposedPrice,
        DateTimeOffset nowUtc,
        DateTimeOffset? lastRepricedAtUtc);
}
