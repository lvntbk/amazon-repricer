namespace AmazonRepricer.Application.Pricing;

public sealed record PriceUpdateSafetyGateResult(
    bool IsAllowed,
    string Reason);

public interface IPriceUpdateSafetyGate
{
    Task<PriceUpdateSafetyGateResult> EvaluateAsync(
        CancellationToken cancellationToken = default);
}
