namespace AmazonRepricer.Worker.Repricing;

public sealed record RepricingGuardResult(
    bool IsAllowed,
    string Reason)
{
    public static RepricingGuardResult Allow() =>
        new(true, "Automatic repricing safety checks passed.");

    public static RepricingGuardResult Reject(string reason) =>
        new(false, reason);
}
