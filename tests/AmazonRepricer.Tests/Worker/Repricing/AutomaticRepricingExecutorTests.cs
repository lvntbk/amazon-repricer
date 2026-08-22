using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;
using AmazonRepricer.Infrastructure.Persistence;
using AmazonRepricer.Worker;
using AmazonRepricer.Worker.Repricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AmazonRepricer.Tests.Worker.Repricing;

public sealed class AutomaticRepricingExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DryRun_DoesNotCallAmazon()
    {
        await using var dbContext = CreateDbContext();
        var updater = new FakeAmazonPriceUpdater();
        var guard = new FakeAutomaticRepricingGuard(
            RepricingGuardResult.Allow());

        var executor = CreateExecutor(
            dbContext,
            updater,
            guard,
            RepricingExecutionMode.DryRun);

        var (product, repricingEvent) = CreateScenario();

        var result = await executor.ExecuteAsync(
            product,
            repricingEvent);

        Assert.False(result.WasAttempted);
        Assert.False(result.WasApplied);
        Assert.Equal(0, updater.CallCount);
        Assert.Equal(RepricingStatus.Pending, repricingEvent.Status);
        Assert.Equal(100m, product.CurrentPrice);
    }

    [Fact]
    public async Task ExecuteAsync_MissingMetadata_DoesNotCallAmazon()
    {
        await using var dbContext = CreateDbContext();
        var updater = new FakeAmazonPriceUpdater();
        var guard = new FakeAutomaticRepricingGuard(
            RepricingGuardResult.Allow());

        var executor = CreateExecutor(
            dbContext,
            updater,
            guard,
            RepricingExecutionMode.Automatic);

        var (product, repricingEvent) = CreateScenario();
        product.ProductType = null;

        var result = await executor.ExecuteAsync(
            product,
            repricingEvent);

        Assert.False(result.WasAttempted);
        Assert.False(result.WasApplied);
        Assert.Equal(0, updater.CallCount);
        Assert.Equal(RepricingStatus.Pending, repricingEvent.Status);
        Assert.Equal(100m, product.CurrentPrice);
    }

    [Fact]
    public async Task ExecuteAsync_GuardRejected_DoesNotCallAmazon()
    {
        await using var dbContext = CreateDbContext();
        var updater = new FakeAmazonPriceUpdater();

        var guard = new FakeAutomaticRepricingGuard(
            RepricingGuardResult.Reject(
                "Maximum price change exceeded."));

        var executor = CreateExecutor(
            dbContext,
            updater,
            guard,
            RepricingExecutionMode.Automatic);

        var (product, repricingEvent) = CreateScenario();

        var result = await executor.ExecuteAsync(
            product,
            repricingEvent);

        Assert.False(result.WasAttempted);
        Assert.False(result.WasApplied);
        Assert.Equal(0, updater.CallCount);
        Assert.Equal(RepricingStatus.Pending, repricingEvent.Status);
        Assert.Equal(100m, product.CurrentPrice);
        Assert.Contains(
            "Maximum price change exceeded",
            result.Reason);
    }

    [Fact]
    public async Task ExecuteAsync_AmazonRejected_MarksFailedAndKeepsPrice()
    {
        await using var dbContext = CreateDbContext();

        var updater = new FakeAmazonPriceUpdater
        {
            Result = new AmazonPriceUpdateResult(
                false,
                null,
                new[] { "Amazon rejected test update." })
        };

        var guard = new FakeAutomaticRepricingGuard(
            RepricingGuardResult.Allow());

        var executor = CreateExecutor(
            dbContext,
            updater,
            guard,
            RepricingExecutionMode.Automatic);

        var (product, repricingEvent) = CreateScenario();

        dbContext.Products.Add(product);
        dbContext.RepricingEvents.Add(repricingEvent);
        await dbContext.SaveChangesAsync();

        var result = await executor.ExecuteAsync(
            product,
            repricingEvent);

        Assert.True(result.WasAttempted);
        Assert.False(result.WasApplied);
        Assert.Equal(1, updater.CallCount);

        Assert.Equal(
            RepricingStatus.Failed,
            repricingEvent.Status);

        Assert.False(repricingEvent.WasApplied);
        Assert.Null(repricingEvent.AppliedPrice);
        Assert.NotNull(repricingEvent.ProcessedAtUtc);
        Assert.NotNull(repricingEvent.ApplicationError);
        Assert.Equal(100m, product.CurrentPrice);
    }

    [Fact]
    public async Task ExecuteAsync_AmazonAccepted_MarksAppliedAndUpdatesPrice()
    {
        await using var dbContext = CreateDbContext();

        var updater = new FakeAmazonPriceUpdater
        {
            Result = new AmazonPriceUpdateResult(
                true,
                "submission-001",
                Array.Empty<string>())
        };

        var guard = new FakeAutomaticRepricingGuard(
            RepricingGuardResult.Allow());

        var executor = CreateExecutor(
            dbContext,
            updater,
            guard,
            RepricingExecutionMode.Automatic);

        var (product, repricingEvent) = CreateScenario();

        dbContext.Products.Add(product);
        dbContext.RepricingEvents.Add(repricingEvent);
        await dbContext.SaveChangesAsync();

        var result = await executor.ExecuteAsync(
            product,
            repricingEvent);

        Assert.True(result.WasAttempted);
        Assert.True(result.WasApplied);
        Assert.Equal(1, updater.CallCount);

        Assert.Equal(
            RepricingStatus.Applied,
            repricingEvent.Status);

        Assert.True(repricingEvent.WasApplied);
        Assert.Equal(99m, repricingEvent.AppliedPrice);
        Assert.NotNull(repricingEvent.ProcessedAtUtc);
        Assert.Null(repricingEvent.ApplicationError);
        Assert.Equal(99m, product.CurrentPrice);
    }

    private static AutomaticRepricingExecutor CreateExecutor(
        RepricerDbContext dbContext,
        IAmazonPriceUpdater updater,
        IAutomaticRepricingGuard guard,
        RepricingExecutionMode executionMode)
    {
        var options = Options.Create(
            new WorkerOptions
            {
                ExecutionMode = executionMode,
                MaxPriceChangePercentage = 10m,
                MinimumRepricingIntervalSeconds = 300
            });

        return new AutomaticRepricingExecutor(
            dbContext,
            updater,
            guard,
            NullLogger<AutomaticRepricingExecutor>.Instance,
            options);
    }

    private static RepricerDbContext CreateDbContext()
    {
        var options =
            new DbContextOptionsBuilder<RepricerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new RepricerDbContext(options);
    }

    private static (Product Product, RepricingEvent Event)
        CreateScenario()
    {
        var store = new AmazonStore
        {
            Name = "Test Store",
            SellerId = "TEST-SELLER",
            MarketplaceId = "TEST-MARKETPLACE",
            IsActive = true
        };

        var product = new Product
        {
            AmazonStoreId = store.Id,
            AmazonStore = store,
            Sku = "TEST-SKU",
            Asin = "B0TEST",
            Title = "Test Product",
            ProductType = "PRODUCT",
            CurrencyCode = "TRY",
            CurrentPrice = 100m,
            IsRepricingEnabled = true
        };

        var repricingEvent = new RepricingEvent
        {
            ProductId = product.Id,
            Product = product,
            OldPrice = 100m,
            ProposedPrice = 99m,
            Reason = "Test repricing decision."
        };

        return (product, repricingEvent);
    }

    private sealed class FakeAutomaticRepricingGuard
        : IAutomaticRepricingGuard
    {
        private readonly RepricingGuardResult _result;

        public FakeAutomaticRepricingGuard(
            RepricingGuardResult result)
        {
            _result = result;
        }

        public RepricingGuardResult Evaluate(
            decimal currentPrice,
            decimal proposedPrice,
            DateTimeOffset nowUtc,
            DateTimeOffset? lastRepricedAtUtc)
        {
            return _result;
        }
    }

    private sealed class FakeAmazonPriceUpdater
        : IAmazonPriceUpdater
    {
        public int CallCount { get; private set; }

        public AmazonPriceUpdateResult Result { get; set; } =
            new(
                true,
                "test-submission",
                Array.Empty<string>());

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

            return Task.FromResult(Result);
        }
    }
}
