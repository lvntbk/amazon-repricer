using AmazonRepricer.Worker.Observability;
using System.Diagnostics.Metrics;

namespace AmazonRepricer.Tests.Worker.Repricing;

public sealed class RepricingMetricsTests
{
    [Fact]
    public void RecordCycle_EmitsCounterAndDuration()
    {
        var measurements =
            new List<MeasurementRecord>();

        using var listener =
            CreateListener(measurements);

        var metrics =
            new RepricingMetrics();

        metrics.RecordCycle(
            "success",
            125.5);

        Assert.Contains(
            measurements,
            measurement =>
                measurement.Name ==
                    "amazon_repricer.worker.cycles" &&
                measurement.Value == 1 &&
                measurement.Outcome == "success");

        Assert.Contains(
            measurements,
            measurement =>
                measurement.Name ==
                    "amazon_repricer.worker.cycle.duration" &&
                measurement.Value == 125.5 &&
                measurement.Outcome == "success");
    }

    [Fact]
    public void RecordExecution_EmitsOutcomeCounter()
    {
        var measurements =
            new List<MeasurementRecord>();

        using var listener =
            CreateListener(measurements);

        var metrics =
            new RepricingMetrics();

        metrics.RecordExecution("applied");

        Assert.Contains(
            measurements,
            measurement =>
                measurement.Name ==
                    "amazon_repricer.repricing.executions" &&
                measurement.Value == 1 &&
                measurement.Outcome == "applied");
    }

    private static MeterListener CreateListener(
        List<MeasurementRecord> measurements)
    {
        var listener =
            new MeterListener
            {
                InstrumentPublished =
                    (instrument, currentListener) =>
                    {
                        if (instrument.Meter.Name ==
                            RepricingMetrics.MeterName)
                        {
                            currentListener.EnableMeasurementEvents(
                                instrument);
                        }
                    }
            };

        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) =>
            {
                measurements.Add(
                    new MeasurementRecord(
                        instrument.Name,
                        value,
                        GetOutcome(tags)));
            });

        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) =>
            {
                measurements.Add(
                    new MeasurementRecord(
                        instrument.Name,
                        value,
                        GetOutcome(tags)));
            });

        listener.Start();

        return listener;
    }

    private static string? GetOutcome(
        ReadOnlySpan<
            KeyValuePair<string, object?>>
            tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == "outcome")
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }

    private sealed record MeasurementRecord(
        string Name,
        double Value,
        string? Outcome);
}
