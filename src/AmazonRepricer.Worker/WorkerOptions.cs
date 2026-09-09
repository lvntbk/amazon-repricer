using AmazonRepricer.Worker.Repricing;

namespace AmazonRepricer.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int IntervalSeconds { get; set; } = 30;

    public RepricingExecutionMode ExecutionMode { get; set; }
        = RepricingExecutionMode.DryRun;

    public int MinimumRepricingIntervalSeconds { get; set; } = 300;

    public int ReconciliationIntervalSeconds { get; set; } = 60;

    public int ReconciliationBatchSize { get; set; } = 100;
}
