namespace AmazonRepricer.Worker.Repricing;

public sealed record AutomaticRepricingExecutionResult(
    bool WasAttempted,
    bool WasApplied,
    string Reason)
{
    public static AutomaticRepricingExecutionResult Skipped(
        string reason) =>
        new(false, false, reason);

    public static AutomaticRepricingExecutionResult Failed(
        string reason) =>
        new(true, false, reason);

    public static AutomaticRepricingExecutionResult Applied() =>
        new(true, true, "Automatic repricing applied.");
}
