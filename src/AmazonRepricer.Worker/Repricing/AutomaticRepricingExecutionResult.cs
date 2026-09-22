namespace AmazonRepricer.Worker.Repricing;

public sealed record AutomaticRepricingExecutionResult(
    bool WasAttempted,
    bool WasApplied,
    string Reason)
{
    public bool IsAwaitingVerification { get; init; }

    public static AutomaticRepricingExecutionResult AwaitingVerification() =>
        new(true, false, "Amazon accepted submission; verification pending.")
        {
            IsAwaitingVerification = true
        };

    public static AutomaticRepricingExecutionResult Skipped(
        string reason) =>
        new(false, false, reason);

    public static AutomaticRepricingExecutionResult Failed(
        string reason) =>
        new(true, false, reason);

    public static AutomaticRepricingExecutionResult Applied() =>
        new(true, true, "Automatic repricing applied.");
}
