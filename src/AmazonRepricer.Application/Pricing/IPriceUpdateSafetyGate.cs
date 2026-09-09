namespace AmazonRepricer.Application.Pricing;

public sealed record PriceUpdateSafetyGateResult(
    bool IsAllowed,
    string Reason,
    decimal MaxPriceChangePercentage = 0m);

public interface IPriceUpdateSafetyGate
{
    Task<PriceUpdateSafetyGateResult> EvaluateAsync(
        CancellationToken cancellationToken = default);
}
