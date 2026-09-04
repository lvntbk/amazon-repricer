using AmazonRepricer.Infrastructure.Persistence;
using AmazonRepricer.Worker.Repricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Worker;

public sealed class Worker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Worker> _logger;
    private readonly WorkerOptions _options;

    public Worker(
        IServiceScopeFactory scopeFactory,
        ILogger<Worker> logger,
        IOptions<WorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;

        if (_options.IntervalSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Worker interval must be greater than zero.");
        }

        if (_options.MaxPriceChangePercentage <= 0 ||
            _options.MaxPriceChangePercentage > 100)
        {
            throw new InvalidOperationException(
                "Maximum price change percentage must be between 0 and 100.");
        }

        if (_options.MinimumRepricingIntervalSeconds < 0)
        {
            throw new InvalidOperationException(
                "Minimum repricing interval cannot be negative.");
        }
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Amazon Repricer Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessProductsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Unexpected error during repricing cycle.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.IntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Amazon Repricer Worker stopped.");
    }

    private async Task ProcessProductsAsync(
        CancellationToken cancellationToken)
    {
        List<Guid> productIds;

        using (var queryScope = _scopeFactory.CreateScope())
        {
            var dbContext = queryScope.ServiceProvider
                .GetRequiredService<RepricerDbContext>();

            productIds = await dbContext.Products
                .AsNoTracking()
                .Where(x =>
                    x.IsRepricingEnabled &&
                    x.PricingRule != null &&
                    x.PricingRule.IsActive &&
                    x.AmazonStore.IsActive)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Found {ProductCount} products eligible for repricing.",
            productIds.Count);

        foreach (var productId in productIds)
        {
            try
            {
                using var productScope =
                    _scopeFactory.CreateScope();

                var processor = productScope.ServiceProvider
                    .GetRequiredService<IProductRepricingProcessor>();

                await processor.ProcessAsync(
                    productId,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to process product {ProductId}.",
                    productId);
            }
        }
    }
}
