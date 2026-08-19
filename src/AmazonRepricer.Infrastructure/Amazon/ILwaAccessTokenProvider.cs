namespace AmazonRepricer.Infrastructure.Amazon;

public interface ILwaAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default);
}
