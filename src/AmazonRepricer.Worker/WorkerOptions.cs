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

    public int VerificationInitialDelaySeconds { get; set; } = 30;

    public int VerificationMaximumDelaySeconds { get; set; } = 900;

    public int VerificationMaximumAttempts { get; set; } = 10;

    public int VerificationLeaseSeconds { get; set; } = 120;

    public int VerificationMaximumObservationAgeSeconds { get; set; } = 120;
}
