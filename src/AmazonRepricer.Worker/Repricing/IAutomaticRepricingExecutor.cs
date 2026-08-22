using AmazonRepricer.Domain.Entities;

namespace AmazonRepricer.Worker.Repricing;

public interface IAutomaticRepricingExecutor
{
    Task<AutomaticRepricingExecutionResult> ExecuteAsync(
        Product product,
        RepricingEvent repricingEvent,
        CancellationToken cancellationToken = default);
}
