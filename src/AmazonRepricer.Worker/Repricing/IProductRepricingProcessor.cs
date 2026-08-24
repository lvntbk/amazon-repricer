namespace AmazonRepricer.Worker.Repricing;

public interface IProductRepricingProcessor
{
    Task ProcessAsync(
        Guid productId,
        CancellationToken cancellationToken = default);
}
