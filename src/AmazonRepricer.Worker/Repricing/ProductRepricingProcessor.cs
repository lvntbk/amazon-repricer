using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Worker.Repricing;

public sealed class ProductRepricingProcessor
    : IProductRepricingProcessor
{
    private readonly RepricerDbContext _dbContext;
    private readonly IPricingEngine _pricingEngine;
    private readonly IAmazonPricingProvider _amazonPricingProvider;
    private readonly IAutomaticRepricingExecutor _automaticRepricingExecutor;
    private readonly ILogger<ProductRepricingProcessor> _logger;
    private readonly WorkerOptions _options;

    public ProductRepricingProcessor(
        RepricerDbContext dbContext,
        IPricingEngine pricingEngine,
        IAmazonPricingProvider amazonPricingProvider,
        IAutomaticRepricingExecutor automaticRepricingExecutor,
        ILogger<ProductRepricingProcessor> logger,
        IOptions<WorkerOptions> options)
    {
        _dbContext = dbContext;
        _pricingEngine = pricingEngine;
        _amazonPricingProvider = amazonPricingProvider;
        _automaticRepricingExecutor = automaticRepricingExecutor;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ProcessAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .Include(x => x.PricingRule)
            .Include(x => x.AmazonStore)
            .FirstOrDefaultAsync(
                x => x.Id == productId,
                cancellationToken);

        if (product is null)
        {
            _logger.LogWarning(
                "Product {ProductId} no longer exists. Repricing skipped.",
                productId);
            return;
        }

        if (!product.IsRepricingEnabled ||
            !product.AmazonStore.IsActive ||
            product.PricingRule is null ||
            !product.PricingRule.IsActive ||
            product.CurrentPrice is null)
        {
            _logger.LogInformation(
                "Product {ProductId}, SKU {Sku} is not eligible for repricing.",
                product.Id,
                product.Sku);
            return;
        }

        var pricingInfo = await GetPricingWithRetryAsync(
            product,
            cancellationToken);

        var currentPrice = product.CurrentPrice.Value;

        var result = _pricingEngine.Calculate(
            currentPrice,
            pricingInfo.FeaturedOfferPrice,
            pricingInfo.IsFeaturedOfferOurs,
            product.PricingRule,
            product.Cost);

        var lastSnapshot = await _dbContext.PriceSnapshots
            .AsNoTracking()
            .Where(x => x.ProductId == product.Id)
            .OrderByDescending(x => x.CapturedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var isDuplicateSnapshot =
            lastSnapshot is not null &&
            lastSnapshot.OurPrice == currentPrice &&
            lastSnapshot.FeaturedOfferPrice ==
                pricingInfo.FeaturedOfferPrice &&
            lastSnapshot.IsFeaturedOfferOurs ==
                pricingInfo.IsFeaturedOfferOurs;

        if (!isDuplicateSnapshot)
        {
            _dbContext.PriceSnapshots.Add(
                new PriceSnapshot
                {
                    ProductId = product.Id,
                    OurPrice = currentPrice,
                    FeaturedOfferPrice = pricingInfo.FeaturedOfferPrice,
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

        if (!result.ShouldChangePrice)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "No repricing required for SKU {Sku}. Reason: {Reason}",
                product.Sku,
                result.Reason);
            return;
        }

        var lastEvent = await _dbContext.RepricingEvents
            .AsNoTracking()
            .Where(x => x.ProductId == product.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var isDuplicateDecision =
            lastEvent is not null &&
            lastEvent.OldPrice == currentPrice &&
            lastEvent.ProposedPrice == result.ProposedPrice &&
            !lastEvent.WasApplied;

        if (isDuplicateDecision)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Duplicate repricing decision skipped for SKU {Sku}.",
                product.Sku);
            return;
        }

        var repricingEvent = new RepricingEvent
        {
            ProductId = product.Id,
            OldPrice = currentPrice,
            ProposedPrice = result.ProposedPrice,
            AppliedPrice = null,
            WasApplied = false,
            Reason = result.Reason
        };

        _dbContext.RepricingEvents.Add(repricingEvent);

        var executionResult =
            await _automaticRepricingExecutor.ExecuteAsync(
                product,
                repricingEvent,
                cancellationToken);

        // DryRun and blocked executions are not persisted by the executor.
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "SKU {Sku}: current {CurrentPrice}, featured offer {FeaturedOfferPrice}, proposed {ProposedPrice}, change {ShouldChange}, automatic attempted {WasAttempted}, automatic applied {WasApplied}, execution reason {ExecutionReason}",
            product.Sku,
            currentPrice,
            pricingInfo.FeaturedOfferPrice,
            result.ProposedPrice,
            result.ShouldChangePrice,
            executionResult.WasAttempted,
            executionResult.WasApplied,
            executionResult.Reason);
    }

    private async Task<AmazonPricingInfo> GetPricingWithRetryAsync(
        Product product,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.MaxRetryAttempts);
        var baseDelaySeconds = Math.Max(1, _options.RetryDelaySeconds);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await _amazonPricingProvider.GetPricingAsync(
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
}
