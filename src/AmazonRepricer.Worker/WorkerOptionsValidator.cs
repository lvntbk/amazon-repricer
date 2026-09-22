using Microsoft.Extensions.Options;

namespace AmazonRepricer.Worker;

public sealed class WorkerOptionsValidator
    : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        WorkerOptions options)
    {
        var failures = new List<string>();

        if (options.IntervalSeconds <= 0)
        {
            failures.Add(
                "Worker interval must be greater than zero.");
        }

        if (options.MinimumRepricingIntervalSeconds <= 0)
        {
            failures.Add(
                "Minimum repricing interval must be greater than zero.");
        }

        if (options.ReconciliationIntervalSeconds <= 0)
        {
            failures.Add(
                "Reconciliation interval must be greater than zero.");
        }

        if (options.ReconciliationBatchSize <= 0)
        {
            failures.Add(
                "Reconciliation batch size must be greater than zero.");
        }

        if (options.VerificationInitialDelaySeconds is < 1 or > 86400)
        {
            failures.Add(
                "Verification initial delay must be between 1 and 86400 seconds.");
        }

        if (options.VerificationMaximumDelaySeconds <
                options.VerificationInitialDelaySeconds ||
            options.VerificationMaximumDelaySeconds > 86400)
        {
            failures.Add(
                "Verification maximum delay must be at least the initial "
                + "delay and at most 86400 seconds.");
        }

        if (options.VerificationMaximumAttempts is < 1 or > 100)
        {
            failures.Add(
                "Verification maximum attempts must be between 1 and 100.");
        }

        if (options.VerificationLeaseSeconds is < 60 or > 3600)
        {
            failures.Add(
                "Verification lease must be between 60 and 3600 seconds.");
        }

        if (options.VerificationMaximumObservationAgeSeconds is < 1 or > 3600)
        {
            failures.Add(
                "Verification observation age must be between 1 and 3600 seconds.");
        }

        if (!Enum.IsDefined(options.ExecutionMode))
        {
            failures.Add(
                "Worker execution mode is invalid.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
