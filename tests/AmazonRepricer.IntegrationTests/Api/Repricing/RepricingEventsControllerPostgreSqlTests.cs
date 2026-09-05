using AmazonRepricer.Api.Controllers;
using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;
using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Infrastructure.Persistence;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.IntegrationTests.Api.Repricing;

[Collection(PostgreSqlCollection.Name)]
public sealed class RepricingEventsControllerPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public RepricingEventsControllerPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task ConcurrentManualApply_CallsAmazonOnlyOnce()
    {
        var scenario = await SeedApprovedScenarioAsync();

        await using var firstContext =
            _database.CreateDbContext();
        await using var duplicateContext =
            _database.CreateDbContext();

        var amazonCallStarted =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var releaseAmazonCall =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var updater = new BlockingAmazonPriceUpdater(
            amazonCallStarted,
            releaseAmazonCall);

        var firstController = CreateController(
            firstContext,
            updater);

        var duplicateController = CreateController(
            duplicateContext,
            updater);

        var firstExecutionTask = firstController.Apply(
            scenario.RepricingEventId,
            CancellationToken.None);

        await amazonCallStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        await using (var observationContext =
            _database.CreateDbContext())
        {
            var statusDuringAmazonCall =
                await observationContext.RepricingEvents
                    .AsNoTracking()
                    .Where(x =>
                        x.Id == scenario.RepricingEventId)
                    .Select(x => x.Status)
                    .SingleAsync();

            Assert.Equal(
                RepricingStatus.Applying,
                statusDuringAmazonCall);
        }

        IActionResult duplicateResult;

        try
        {
            duplicateResult =
                await duplicateController.Apply(
                    scenario.RepricingEventId,
                    CancellationToken.None);
        }
        finally
        {
            releaseAmazonCall.TrySetResult(true);
        }

        var firstResult = await firstExecutionTask;

        Assert.IsType<OkObjectResult>(firstResult);
        Assert.IsType<ConflictObjectResult>(duplicateResult);
        Assert.Equal(1, updater.CallCount);

        await using var verificationContext =
            _database.CreateDbContext();

        var persistedEvent =
            await verificationContext.RepricingEvents
                .AsNoTracking()
                .SingleAsync(x =>
                    x.Id == scenario.RepricingEventId);

        var persistedPrice =
            await verificationContext.Products
                .AsNoTracking()
                .Where(x => x.Id == scenario.ProductId)
                .Select(x => x.CurrentPrice)
                .SingleAsync();

        Assert.Equal(
            RepricingStatus.Applied,
            persistedEvent.Status);
        Assert.True(persistedEvent.WasApplied);
        Assert.Equal(99m, persistedEvent.AppliedPrice);
        Assert.Equal(
            "submission-manual-claim-001",
            persistedEvent.AmazonSubmissionId);
        Assert.Equal(99m, persistedPrice);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(150)]
    public async Task ManualApply_PriceOutsidePricingRuleBounds_DoesNotCallAmazon(
        int proposedPrice)
    {
        var suffix = Guid.NewGuid().ToString("N");

        var store = new AmazonStore
        {
            Name = $"Safety Store {suffix}",
            SellerId = $"SELLER-{suffix}",
            MarketplaceId = "A33AVAJ2PDY3EV",
            IsActive = true
        };

        var product = new Product
        {
            AmazonStoreId = store.Id,
            AmazonStore = store,
            Sku = $"SAFETY-{suffix}",
            Asin = "B0SAFETYTEST",
            Title = "Safety boundary integration product",
            ProductType = "PRODUCT",
            CurrencyCode = "TRY",
            CurrentPrice = 100m,
            IsRepricingEnabled = true
        };

        var pricingRule = new PricingRule
        {
            ProductId = product.Id,
            Product = product,
            Strategy = PricingStrategy.MatchFeaturedOffer,
            MinimumPrice = 90m,
            MaximumPrice = 110m,
            AdjustmentValue = 0m,
            IsActive = true
        };

        var repricingEvent = new RepricingEvent
        {
            ProductId = product.Id,
            Product = product,
            OldPrice = 100m,
            ProposedPrice = proposedPrice,
            Reason = "Manual apply hard price boundary test."
        };

        repricingEvent.Approve(
            "Approved to verify submission-time safety.");

        await using (var seedContext = _database.CreateDbContext())
        {
            seedContext.Products.Add(product);
            seedContext.Set<PricingRule>().Add(pricingRule);
            seedContext.RepricingEvents.Add(repricingEvent);
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = _database.CreateDbContext();

        var amazonCallStarted =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var releaseAmazonCall =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        releaseAmazonCall.SetResult(true);

        var updater = new BlockingAmazonPriceUpdater(
            amazonCallStarted,
            releaseAmazonCall);

        var controller = CreateController(
            dbContext,
            updater);

        var result = await controller.Apply(
            repricingEvent.Id,
            CancellationToken.None);

        Assert.Equal(0, updater.CallCount);
        Assert.IsType<ConflictObjectResult>(result);
    }

    private async Task<ScenarioIds>
        SeedApprovedScenarioAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");

        var store = new AmazonStore
        {
            Name = $"Manual Apply Store {suffix}",
            SellerId = $"SELLER-{suffix}",
            MarketplaceId = "A33AVAJ2PDY3EV",
            IsActive = true
        };

        var product = new Product
        {
            AmazonStoreId = store.Id,
            AmazonStore = store,
            Sku = $"MANUAL-{suffix}",
            Asin = "B0MANUALAPPLY",
            Title = "Manual apply integration product",
            ProductType = "PRODUCT",
            CurrencyCode = "TRY",
            CurrentPrice = 100m,
            IsRepricingEnabled = true
        };

        var pricingRule = new PricingRule
        {
            ProductId = product.Id,
            Product = product,
            Strategy = PricingStrategy.MatchFeaturedOffer,
            MinimumPrice = 90m,
            MaximumPrice = 110m,
            AdjustmentValue = 0m,
            IsActive = true
        };

        var repricingEvent = new RepricingEvent
        {
            ProductId = product.Id,
            Product = product,
            OldPrice = 100m,
            ProposedPrice = 99m,
            Reason = "Manual apply concurrency test."
        };

        repricingEvent.Approve(
            "Approved for manual application.");

        await using var dbContext =
            _database.CreateDbContext();

        dbContext.Products.Add(product);
        dbContext.Set<PricingRule>().Add(pricingRule);
        dbContext.RepricingEvents.Add(repricingEvent);
        await dbContext.SaveChangesAsync();

        return new ScenarioIds(
            product.Id,
            repricingEvent.Id);
    }

    private static RepricingEventsController CreateController(
        RepricerDbContext dbContext,
        IAmazonPriceUpdater updater)
    {
        var options = Options.Create(
            new AmazonSpApiOptions
            {
                DefaultProductType = "PRODUCT",
                CurrencyCode = "TRY"
            });

        return new RepricingEventsController(
            dbContext,
            updater,
            options,
            NullLogger<RepricingEventsController>.Instance);
    }

    private sealed record ScenarioIds(
        Guid ProductId,
        Guid RepricingEventId);

    private sealed class BlockingAmazonPriceUpdater
        : IAmazonPriceUpdater
    {
        private readonly TaskCompletionSource<bool>
            _amazonCallStarted;
        private readonly TaskCompletionSource<bool>
            _releaseAmazonCall;
        private int _callCount;

        public BlockingAmazonPriceUpdater(
            TaskCompletionSource<bool> amazonCallStarted,
            TaskCompletionSource<bool> releaseAmazonCall)
        {
            _amazonCallStarted = amazonCallStarted;
            _releaseAmazonCall = releaseAmazonCall;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public async Task<AmazonPriceUpdateResult>
            UpdatePriceAsync(
                string sellerId,
                string sku,
                string marketplaceId,
                string productType,
                decimal price,
                string currencyCode,
                CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            _amazonCallStarted.TrySetResult(true);

            await _releaseAmazonCall.Task.WaitAsync(
                cancellationToken);

            return new AmazonPriceUpdateResult(
                true,
                "submission-manual-claim-001",
                Array.Empty<string>());
        }
    }
}
