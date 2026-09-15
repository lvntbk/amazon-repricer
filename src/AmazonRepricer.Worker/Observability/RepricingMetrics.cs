using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AmazonRepricer.Worker.Observability;

public sealed class RepricingMetrics
{
    public const string MeterName =
        "AmazonRepricer.Worker";

    private static readonly Meter Meter =
        new(MeterName);

    private static readonly Counter<long> Cycles =
        Meter.CreateCounter<long>(
            "amazon_repricer.worker.cycles");

    private static readonly Histogram<double>
        CycleDuration =
            Meter.CreateHistogram<double>(
                "amazon_repricer.worker.cycle.duration",
                unit: "ms");

    private static readonly Counter<long> Executions =
        Meter.CreateCounter<long>(
            "amazon_repricer.repricing.executions");

    public void RecordCycle(
        string outcome,
        double elapsedMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            outcome);

        if (elapsedMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedMs));
        }

        var tags =
            new TagList
            {
                { "outcome", outcome }
            };

        Cycles.Add(
            1,
            tags);

        CycleDuration.Record(
            elapsedMs,
            tags);
    }

    public void RecordExecution(
        string outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            outcome);

        Executions.Add(
            1,
            new TagList
            {
                { "outcome", outcome }
            });
    }
}
