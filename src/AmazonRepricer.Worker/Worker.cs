using AmazonRepricer.Worker.Repricing;
using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Infrastructure.Persistence;
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

        if (_options.MaxRetryAttempts <= 0)
        {
            throw new InvalidOperationException(
                "Maximum retry attempts must be greater than zero.");
        }

        if (_options.RetryDelaySeconds <= 0)
        {
            throw new InvalidOperationException(
                "Retry delay must be greater than zero.");
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
        using var scope = _scopeFactory.CreateScope();

        var dbContext =
            scope.ServiceProvider.GetRequiredService<RepricerDbContext>();

        var pricingEngine =
            scope.ServiceProvider.GetRequiredService<IPricingEngine>();

        var automaticRepricingGuard =
            scope.ServiceProvider.GetRequiredService<
                IAutomaticRepricingGuard>();

        var amazonPricingProvider =
            scope.ServiceProvider.GetRequiredService<IAmazonPricingProvider>();

        var amazonPriceUpdater =
            scope.ServiceProvider.GetRequiredService<IAmazonPriceUpdater>();

        var products = await dbContext.Products
            .Include(x => x.PricingRule)
            .Include(x => x.AmazonStore)
            .Where(x =>
                x.IsRepricingEnabled &&
                x.PricingRule != null &&
                x.PricingRule.IsActive &&
                x.AmazonStore.IsActive)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Found {ProductCount} products eligible for repricing.",
            products.Count);

        foreach (var product in products)
        {
            try
            {
                await ProcessProductAsync(
                    dbContext,
                    pricingEngine,
                    amazonPricingProvider,
                    amazonPriceUpdater,
                    automaticRepricingGuard,
                    product,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to process product {Sku}.",
                    product.Sku);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }


    private async Task<AmazonPricingInfo> GetPricingWithRetryAsync(
        IAmazonPricingProvider amazonPricingProvider,
        Product product,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.MaxRetryAttempts);
        var baseDelaySeconds = Math.Max(1, _options.RetryDelaySeconds);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await amazonPricingProvider.GetPricingAsync(
                    product.Asin,
                    product.Sku,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(
                    baseDelaySeconds * Math.Pow(2, attempt - 1));

                _logger.LogWarning(
                    exception,
                    "Amazon pricing request failed for SKU {Sku}. Attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds} seconds.",
                    product.Sku,
                    attempt,
                    maxAttempts,
                    delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Amazon pricing request failed after {maxAttempts} attempts for SKU {product.Sku}.");
    }


    private async Task ProcessProductAsync(
        RepricerDbContext dbContext,
        IPricingEngine pricingEngine,
        IAmazonPricingProvider amazonPricingProvider,
        IAmazonPriceUpdater amazonPriceUpdater,
        IAutomaticRepricingGuard automaticRepricingGuard,
        Product product,
        CancellationToken cancellationToken)
    {
        if (product.CurrentPrice is null ||
            product.PricingRule is null)
        {
            return;
        }

        var pricingInfo =
            await GetPricingWithRetryAsync(
                amazonPricingProvider,
                product,
                cancellationToken);

        var result = pricingEngine.Calculate(
            product.CurrentPrice.Value,
            pricingInfo.FeaturedOfferPrice,
            pricingInfo.IsFeaturedOfferOurs,
            product.PricingRule,
            product.Cost);

        var lastSnapshot = await dbContext.PriceSnapshots
            .Where(x => x.ProductId == product.Id)
            .OrderByDescending(x => x.CapturedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var isDuplicateSnapshot =
            lastSnapshot is not null &&
            lastSnapshot.OurPrice == product.CurrentPrice.Value &&
            lastSnapshot.FeaturedOfferPrice ==
                pricingInfo.FeaturedOfferPrice &&
            lastSnapshot.IsFeaturedOfferOurs ==
                pricingInfo.IsFeaturedOfferOurs;

        if (!isDuplicateSnapshot)
        {
            dbContext.PriceSnapshots.Add(
                new PriceSnapshot
                {
                    ProductId = product.Id,
                    OurPrice = product.CurrentPrice.Value,
                    FeaturedOfferPrice =
                        pricingInfo.FeaturedOfferPrice,
                    IsFeaturedOfferOurs =
                        pricingInfo.IsFeaturedOfferOurs
                });
        }
        else
        {
            _logger.LogInformation(
                "Duplicate price snapshot skipped for SKU {Sku}.",
                product.Sku);
        }

        var lastEvent = await dbContext.RepricingEvents
            .Where(x => x.ProductId == product.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (!result.ShouldChangePrice)
        {
            _logger.LogInformation(
                "No repricing required for SKU {Sku}. Reason: {Reason}",
                product.Sku,
                result.Reason);

            return;
        }

        var isDuplicateDecision =
            lastEvent is not null &&
            lastEvent.OldPrice == product.CurrentPrice.Value &&
            lastEvent.ProposedPrice == result.ProposedPrice &&
            lastEvent.WasApplied == false;

        if (isDuplicateDecision)
        {
            _logger.LogInformation(
                "Duplicate repricing decision skipped for SKU {Sku}.",
                product.Sku);

            return;
        }

        var repricingEvent = new RepricingEvent
        {
            ProductId = product.Id,
            OldPrice = product.CurrentPrice.Value,
            ProposedPrice = result.ProposedPrice,
            AppliedPrice = null,
            WasApplied = false,
            Reason = result.Reason
        };

        dbContext.RepricingEvents.Add(repricingEvent);

        if (_options.ExecutionMode !=
            RepricingExecutionMode.Automatic)
        {
            _logger.LogInformation(
                "Repricing decision for SKU {Sku} recorded in {ExecutionMode} mode. No automatic price update will be sent.",
                product.Sku,
                _options.ExecutionMode);

            return;
        }

        if (string.IsNullOrWhiteSpace(product.ProductType) ||
            string.IsNullOrWhiteSpace(product.CurrencyCode))
        {
            repricingEvent.MarkFailed(
                "Automatic repricing blocked because Amazon listing metadata is incomplete.");

            _logger.LogWarning(
                "Automatic repricing blocked for SKU {Sku}: ProductType or CurrencyCode is missing.",
                product.Sku);

            return;
        }

        var lastAppliedEvent = await dbContext.RepricingEvents
            .Where(x =>
                x.ProductId == product.Id &&
                x.WasApplied &&
                x.ProcessedAtUtc != null)
            .OrderByDescending(x => x.ProcessedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var lastRepricedAtUtc =
            lastAppliedEvent?.ProcessedAtUtc is DateTime processedAtUtc
                ? new DateTimeOffset(
                    DateTime.SpecifyKind(
                        processedAtUtc,
                        DateTimeKind.Utc))
                : (DateTimeOffset?)null;

        var guardResult = automaticRepricingGuard.Evaluate(
            product.CurrentPrice.Value,
            result.ProposedPrice,
            DateTimeOffset.UtcNow,
            lastRepricedAtUtc);

        if (!guardResult.IsAllowed)
        {
            repricingEvent.MarkFailed(
                $"Automatic repricing blocked: {guardResult.Reason}");

            _logger.LogWarning(
                "Automatic repricing guard blocked SKU {Sku}: {Reason}",
                product.Sku,
                guardResult.Reason);

            return;
        }

        repricingEvent.ApproveAutomatically(
            $"Automatic repricing approved. {guardResult.Reason}");

        // Persist the pricing intent before performing the external side effect.
        await dbContext.SaveChangesAsync(cancellationToken);

        AmazonPriceUpdateResult updateResult;

        try
        {
            updateResult = await amazonPriceUpdater.UpdatePriceAsync(
                product.AmazonStore.SellerId,
                product.Sku,
                product.AmazonStore.MarketplaceId,
                product.ProductType,
                result.ProposedPrice,
                product.CurrencyCode,
                cancellationToken);
        }
        catch (Exception exception)
        {
            repricingEvent.MarkFailed(
                $"Amazon price update failed before acceptance: {exception.Message}");

            await dbContext.SaveChangesAsync(cancellationToken);

            throw;
        }

        if (!updateResult.Accepted)
        {
            var issues = updateResult.Issues.Count == 0
                ? "Amazon rejected the price update."
                : string.Join("; ", updateResult.Issues);

            repricingEvent.MarkFailed(issues);

            _logger.LogWarning(
                "Amazon rejected automatic repricing for SKU {Sku}: {Issues}",
                product.Sku,
                issues);

            return;
        }

        repricingEvent.MarkApplied(result.ProposedPrice);
        product.CurrentPrice = result.ProposedPrice;

        _logger.LogInformation(
            "Automatic repricing submission accepted for SKU {Sku}. Price {OldPrice} -> {NewPrice}. SubmissionId: {SubmissionId}",
            product.Sku,
            repricingEvent.OldPrice,
            result.ProposedPrice,
            updateResult.SubmissionId);
        _logger.LogInformation(
            "SKU {Sku}: current {CurrentPrice}, featured offer {FeaturedOfferPrice}, proposed {ProposedPrice}, change {ShouldChange}",
            product.Sku,
            product.CurrentPrice,
            pricingInfo.FeaturedOfferPrice,
            result.ProposedPrice,
            result.ShouldChangePrice);
    }
}
