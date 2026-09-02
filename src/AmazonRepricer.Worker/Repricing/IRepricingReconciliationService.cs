namespace AmazonRepricer.Worker.Repricing;

public interface IRepricingReconciliationService
{
    Task<int> ReconcileAsync(
        int batchSize,
        CancellationToken cancellationToken = default);
}
