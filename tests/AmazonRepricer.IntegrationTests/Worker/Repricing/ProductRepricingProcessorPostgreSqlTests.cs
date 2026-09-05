using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;
using AmazonRepricer.Infrastructure.Pricing;
using AmazonRepricer.IntegrationTests.PostgreSql;
using AmazonRepricer.Worker;
using AmazonRepricer.Worker.Repricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.IntegrationTests.Worker.Repricing;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProductRepricingProcessorPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public ProductRepricingProcessorPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task CompleteFlow_ReadsAmazonPrice_CalculatesAndPersistsAppliedPrice()
    {
        var scenario = await SeedScenarioAsync();
        var pricingProvider = new StubAmazonPricingProvider(
            new AmazonPricingInfo(
                FeaturedOfferPrice: 99.90m,
                IsFeaturedOfferOurs: false));

        var applicationClaimObserved = false;
        var snapshotObservedBeforeSubmission = false;

        var priceUpdater = new RecordingAmazonPriceUpdater(
            async () =>
            {
                await using var observationContext =
                    _database.CreateDbContext();

                applicationClaimObserved = await observationContext
                    .RepricingEvents
                    .AnyAsync(x =>
                        x.ProductId == scenario.ProductId &&
                        x.Status == RepricingStatus.Applying);

                snapshotObservedBeforeSubmission = await observationContext
                    .PriceSnapshots
                    .AnyAsync(x =>
                        x.ProductId == scenario.ProductId &&
                        x.OurPrice == 100m &&
                        x.FeaturedOfferPrice == 99.90m);

                return new AmazonPriceUpdateResult(
                    true,
                    "submission-e2e-001",
                    Array.Empty<string>());
            });

        await using (var executionContext =
            _database.CreateDbContext())
        {
            var options = Options.Create(new WorkerOptions
            {
                ExecutionMode = RepricingExecutionMode.Automatic,
                MaxPriceChangePercentage = 10m,
                MinimumRepricingIntervalSeconds = 0
            });

            var executor = new AutomaticRepricingExecutor(
                executionContext,
                priceUpdater,
                new DbPriceUpdateSafetyGate(executionContext),
                new AutomaticRepricingGuard(options),
                NullLogger<AutomaticRepricingExecutor>.Instance,
                options);

            var processor = new ProductRepricingProcessor(
                executionContext,
                new PricingEngine(),
                pricingProvider,
                executor,
                NullLogger<ProductRepricingProcessor>.Instance,
                options);

            await processor.ProcessAsync(scenario.ProductId);
        }

        await using var verificationContext =
            _database.CreateDbContext();

        var product = await verificationContext.Products
            .AsNoTracking()
            .SingleAsync(x => x.Id == scenario.ProductId);

        var snapshot = await verificationContext.PriceSnapshots
            .AsNoTracking()
            .SingleAsync(x => x.ProductId == scenario.ProductId);

        var repricingEvent = await verificationContext.RepricingEvents
            .AsNoTracking()
            .SingleAsync(x => x.ProductId == scenario.ProductId);

        Assert.True(applicationClaimObserved);
        Assert.True(snapshotObservedBeforeSubmission);
        Assert.Equal(1, pricingProvider.CallCount);
        Assert.Equal(1, priceUpdater.CallCount);
        Assert.Equal(scenario.SellerId, priceUpdater.SellerId);
        Assert.Equal(scenario.Sku, priceUpdater.Sku);
        Assert.Equal("A33AVAJ2PDY3EV", priceUpdater.MarketplaceId);
        Assert.Equal("PRODUCT", priceUpdater.ProductType);
        Assert.Equal("TRY", priceUpdater.CurrencyCode);
        Assert.Equal(98.90m, priceUpdater.Price);

        Assert.Equal(100m, snapshot.OurPrice);
        Assert.Equal(99.90m, snapshot.FeaturedOfferPrice);
        Assert.False(snapshot.IsFeaturedOfferOurs);

        Assert.Equal(RepricingStatus.Applied, repricingEvent.Status);
        Assert.Equal(100m, repricingEvent.OldPrice);
        Assert.Equal(98.90m, repricingEvent.ProposedPrice);
        Assert.Equal(98.90m, repricingEvent.AppliedPrice);
        Assert.True(repricingEvent.WasApplied);
        Assert.NotNull(repricingEvent.ReviewedAtUtc);
        Assert.NotNull(repricingEvent.ProcessedAtUtc);
        Assert.Null(repricingEvent.ApplicationError);
        Assert.Equal(98.90m, product.CurrentPrice);
    }

    private async Task<Scenario> SeedScenarioAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");

        var store = new AmazonStore
        {
            Name = $"E2E Store {suffix}",
            SellerId = $"SELLER-E2E-{suffix}",
            MarketplaceId = "A33AVAJ2PDY3EV",
            IsActive = true
        };

        var product = new Product
        {
            AmazonStoreId = store.Id,
            AmazonStore = store,
            Sku = $"SKU-E2E-{suffix}",
            Asin = "B0E2ETEST",
            Title = "End-to-end repricing test product",
            ProductType = "PRODUCT",
            CurrencyCode = "TRY",
            Cost = 70m,
            CurrentPrice = 100m,
            IsRepricingEnabled = true
        };

        var rule = new PricingRule
        {
            ProductId = product.Id,
            Product = product,
            Strategy = PricingStrategy.BelowFeaturedOfferByAmount,
            MinimumPrice = 80m,
            MaximumPrice = 120m,
            AdjustmentValue = 1m,
            MinimumProfitPercentage = 10m,
            IsActive = true
        };

        product.PricingRule = rule;

        await using var dbContext = _database.CreateDbContext();

        var safetySettings =
            await dbContext.RepricingSafetySettings
                .SingleAsync(
                    x => x.Id == RepricingSafetySettings.GlobalId);

        safetySettings.PriceUpdatesEnabled = true;
        safetySettings.UpdatedAtUtc = DateTime.UtcNow;

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        return new Scenario(
            product.Id,
            store.SellerId,
            product.Sku);
    }

    private sealed record Scenario(
        Guid ProductId,
        string SellerId,
        string Sku);

    private sealed class StubAmazonPricingProvider
        : IAmazonPricingProvider
    {
        private readonly AmazonPricingInfo _result;

        public StubAmazonPricingProvider(AmazonPricingInfo result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<AmazonPricingInfo> GetPricingAsync(
            string asin,
            string sku,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingAmazonPriceUpdater
        : IAmazonPriceUpdater
    {
        private readonly Func<Task<AmazonPriceUpdateResult>> _update;

        public RecordingAmazonPriceUpdater(
            Func<Task<AmazonPriceUpdateResult>> update)
        {
            _update = update;
        }

        public int CallCount { get; private set; }
        public string? SellerId { get; private set; }
        public string? Sku { get; private set; }
        public string? MarketplaceId { get; private set; }
        public string? ProductType { get; private set; }
        public decimal? Price { get; private set; }
        public string? CurrencyCode { get; private set; }

        public Task<AmazonPriceUpdateResult> UpdatePriceAsync(
            string sellerId,
            string sku,
            string marketplaceId,
            string productType,
            decimal price,
            string currencyCode,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            SellerId = sellerId;
            Sku = sku;
            MarketplaceId = marketplaceId;
            ProductType = productType;
            Price = price;
            CurrencyCode = currencyCode;

            return _update();
        }
    }
}
