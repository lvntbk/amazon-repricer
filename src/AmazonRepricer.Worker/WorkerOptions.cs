using AmazonRepricer.Worker.Repricing;

namespace AmazonRepricer.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int IntervalSeconds { get; set; } = 30;

    public int MaxRetryAttempts { get; set; } = 3;

    public int RetryDelaySeconds { get; set; } = 2;

    public RepricingExecutionMode ExecutionMode { get; set; }
        = RepricingExecutionMode.DryRun;

    public decimal MaxPriceChangePercentage { get; set; } = 10m;

    public int MinimumRepricingIntervalSeconds { get; set; } = 300;
}
