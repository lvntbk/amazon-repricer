using Microsoft.Extensions.Options;

namespace AmazonRepricer.Worker.Repricing;

public sealed class RepricingReconciliationWorker
    : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RepricingReconciliationWorker> _logger;
    private readonly WorkerOptions _options;

    public RepricingReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<RepricingReconciliationWorker> logger,
        IOptions<WorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;

        if (_options.ReconciliationIntervalSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Reconciliation interval must be greater than zero.");
        }

        if (_options.ReconciliationBatchSize <= 0)
        {
            throw new InvalidOperationException(
                "Reconciliation batch size must be greater than zero.");
        }
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Repricing reconciliation worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope =
                    _scopeFactory.CreateAsyncScope();

                var reconciliationService =
                    scope.ServiceProvider.GetRequiredService<
                        IRepricingReconciliationService>();

                var reconciledCount =
                    await reconciliationService.ReconcileAsync(
                        _options.ReconciliationBatchSize,
                        stoppingToken);

                var verificationService =
                    scope.ServiceProvider.GetRequiredService<
                        RepricingVerificationService>();

                var verifiedCount = await verificationService.VerifyAsync(
                    _options.ReconciliationBatchSize,
                    stoppingToken);

                if (verifiedCount > 0)
                {
                    _logger.LogInformation(
                        "Verified {VerifiedCount} Amazon price updates.",
                        verifiedCount);
                }

                if (reconciledCount > 0)
                {
                    _logger.LogWarning(
                        "Reconciled {ReconciledCount} incomplete repricing events.",
                        reconciledCount);
                }
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
                    "Unexpected error during repricing reconciliation.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(
                        _options.ReconciliationIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation(
            "Repricing reconciliation worker stopped.");
    }
}
