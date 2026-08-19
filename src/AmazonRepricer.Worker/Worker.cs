using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Worker;

public sealed class Worker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IServiceScopeFactory scopeFactory,
        ILogger<Worker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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
                    TimeSpan.FromSeconds(30),
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

        var amazonPricingProvider =
            scope.ServiceProvider.GetRequiredService<IAmazonPricingProvider>();

        var products = await dbContext.Products
            .Include(x => x.PricingRule)
            .Where(x =>
                x.IsRepricingEnabled &&
                x.PricingRule != null &&
                x.PricingRule.IsActive)
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

    private async Task ProcessProductAsync(
        RepricerDbContext dbContext,
        IPricingEngine pricingEngine,
        IAmazonPricingProvider amazonPricingProvider,
        Product product,
        CancellationToken cancellationToken)
    {
        if (product.CurrentPrice is null ||
            product.PricingRule is null)
        {
            return;
        }

        var pricingInfo =
            await amazonPricingProvider.GetPricingAsync(
                product.Asin,
                product.Sku,
                cancellationToken);

        var result = pricingEngine.Calculate(
            product.CurrentPrice.Value,
            pricingInfo.FeaturedOfferPrice,
            pricingInfo.IsFeaturedOfferOurs,
            product.PricingRule,
            product.Cost);

        dbContext.PriceSnapshots.Add(
            new PriceSnapshot
            {
                ProductId = product.Id,
                OurPrice = product.CurrentPrice.Value,
                FeaturedOfferPrice = pricingInfo.FeaturedOfferPrice,
                IsFeaturedOfferOurs = pricingInfo.IsFeaturedOfferOurs
            });

        var lastEvent = await dbContext.RepricingEvents
            .Where(x => x.ProductId == product.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var isDuplicateDecision =
            lastEvent is not null &&
            lastEvent.OldPrice == product.CurrentPrice.Value &&
            lastEvent.ProposedPrice == result.ProposedPrice &&
            lastEvent.WasApplied == false;

        if (!isDuplicateDecision)
        {
            dbContext.RepricingEvents.Add(
                new RepricingEvent
                {
                    ProductId = product.Id,
                    OldPrice = product.CurrentPrice.Value,
                    ProposedPrice = result.ProposedPrice,
                    AppliedPrice = null,
                    WasApplied = false,
                    Reason = result.Reason
                });
        }
        else
        {
            _logger.LogInformation(
                "Duplicate repricing decision skipped for SKU {Sku}.",
                product.Sku);
        }

        _logger.LogInformation(
            "SKU {Sku}: current {CurrentPrice}, featured offer {FeaturedOfferPrice}, proposed {ProposedPrice}, change {ShouldChange}",
            product.Sku,
            product.CurrentPrice,
            pricingInfo.FeaturedOfferPrice,
            result.ProposedPrice,
            result.ShouldChangePrice);
    }
}
