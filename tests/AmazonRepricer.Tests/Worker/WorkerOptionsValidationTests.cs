using AmazonRepricer.Worker;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Tests.Worker;

public sealed class WorkerOptionsValidationTests
{
    [Fact]
    public void Validate_InvalidIntervals_Fails()
    {
        var options = new WorkerOptions
        {
            IntervalSeconds = 0,
            MinimumRepricingIntervalSeconds = 0,
            ReconciliationIntervalSeconds = 0,
            ReconciliationBatchSize = 0
        };

        var validator = new WorkerOptionsValidator();

        var result = validator.Validate(null, options);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var options = new WorkerOptions();

        var validator = new WorkerOptionsValidator();

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_InvalidExecutionMode_Fails()
    {
        var options = new WorkerOptions
        {
            ExecutionMode =
                (AmazonRepricer.Worker.Repricing.RepricingExecutionMode)999
        };

        var validator = new WorkerOptionsValidator();

        var result = validator.Validate(null, options);

        Assert.False(result.Succeeded);
    }

}
