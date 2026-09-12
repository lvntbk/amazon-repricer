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
