using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;
using AmazonRepricer.Infrastructure.Persistence;
using AmazonRepricer.IntegrationTests.PostgreSql;
using AmazonRepricer.Worker;
using AmazonRepricer.Worker.Repricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.IntegrationTests.Worker.Repricing;

[Collection(PostgreSqlCollection.Name)]
public sealed class AutomaticRepricingExecutorPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public AutomaticRepricingExecutorPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task AcceptedUpdate_PersistsApprovedIntentBeforeAmazonCall_ThenAppliesPrice()
    {
        var scenario = await SeedScenarioAsync();
        var approvedIntentWasPersisted = false;

        var updater = new DelegatingAmazonPriceUpdater(
            async () =>
            {
                await using var observationContext =
                    _database.CreateDbContext();

                var persistedStatus = await observationContext
                    .RepricingEvents
                    .Where(x => x.Id == scenario.RepricingEventId)
                    .Select(x => x.Status)
                    .SingleAsync();

                approvedIntentWasPersisted =
                    persistedStatus == RepricingStatus.Approved;

                return Accepted("submission-postgresql-001");
            });

        await using (var executionContext =
            _database.CreateDbContext())
        {
            var (product, repricingEvent) = await LoadScenarioAsync(
                executionContext,
                scenario);

            var result = await CreateExecutor(
                    executionContext,
                    updater)
                .ExecuteAsync(product, repricingEvent);

            Assert.True(result.WasAttempted);
            Assert.True(result.WasApplied);
        }

        await using var verificationContext =
            _database.CreateDbContext();

        var persistedEvent = await verificationContext.RepricingEvents
            .AsNoTracking()
            .SingleAsync(x => x.Id == scenario.RepricingEventId);

        var persistedPrice = await verificationContext.Products
            .AsNoTracking()
            .Where(x => x.Id == scenario.ProductId)
            .Select(x => x.CurrentPrice)
            .SingleAsync();

        Assert.True(approvedIntentWasPersisted);
        Assert.Equal(RepricingStatus.Applied, persistedEvent.Status);
        Assert.True(persistedEvent.WasApplied);
        Assert.Equal(99m, persistedEvent.AppliedPrice);
        Assert.Null(persistedEvent.ApplicationError);
        Assert.NotNull(persistedEvent.ProcessedAtUtc);
        Assert.Equal(99m, persistedPrice);
        Assert.Equal(1, updater.CallCount);
    }

    [Fact]
    public async Task RejectedUpdate_PersistsFailedEvent_AndKeepsCurrentPrice()
    {
        var scenario = await SeedScenarioAsync();
        var updater = new DelegatingAmazonPriceUpdater(
            () => Task.FromResult(
                new AmazonPriceUpdateResult(
                    false,
                    null,
                    new[] { "Amazon rejected integration test update." })));

        await using (var executionContext =
            _database.CreateDbContext())
        {
            var (product, repricingEvent) = await LoadScenarioAsync(
                executionContext,
                scenario);

            var result = await CreateExecutor(
                    executionContext,
                    updater)
                .ExecuteAsync(product, repricingEvent);

            Assert.True(result.WasAttempted);
            Assert.False(result.WasApplied);
        }

        await AssertFailedStateAsync(
            scenario,
            "Amazon rejected integration test update.");

        Assert.Equal(1, updater.CallCount);
    }

    [Fact]
    public async Task UpdaterException_PersistsFailedEvent_AndKeepsCurrentPrice()
    {
        var scenario = await SeedScenarioAsync();
        var updater = new DelegatingAmazonPriceUpdater(
            () => throw new HttpRequestException(
                "Simulated Amazon timeout."));

        await using (var executionContext =
            _database.CreateDbContext())
        {
            var (product, repricingEvent) = await LoadScenarioAsync(
                executionContext,
                scenario);

            var result = await CreateExecutor(
                    executionContext,
                    updater)
                .ExecuteAsync(product, repricingEvent);

            Assert.True(result.WasAttempted);
            Assert.False(result.WasApplied);
        }

        await AssertFailedStateAsync(
            scenario,
            "Simulated Amazon timeout.");

        Assert.Equal(1, updater.CallCount);
    }

    [Fact]
    public async Task FinalPersistenceFailure_LeavesApprovedIntent_ForReconciliation()
    {
        var scenario = await SeedScenarioAsync();
        var updater = new DelegatingAmazonPriceUpdater(
            () => Task.FromResult(Accepted("submission-before-db-failure")));

        await using (var executionContext =
            _database.CreateDbContext(
                new FailAppliedPersistenceInterceptor()))
        {
            var (product, repricingEvent) = await LoadScenarioAsync(
                executionContext,
                scenario);

            await Assert.ThrowsAsync<SimulatedPersistenceException>(
                () => CreateExecutor(executionContext, updater)
                    .ExecuteAsync(product, repricingEvent));
        }

        await using var verificationContext =
            _database.CreateDbContext();

        var persistedEvent = await verificationContext.RepricingEvents
            .AsNoTracking()
            .SingleAsync(x => x.Id == scenario.RepricingEventId);

        var persistedPrice = await verificationContext.Products
            .AsNoTracking()
            .Where(x => x.Id == scenario.ProductId)
            .Select(x => x.CurrentPrice)
            .SingleAsync();

        Assert.Equal(RepricingStatus.Approved, persistedEvent.Status);
        Assert.False(persistedEvent.WasApplied);
        Assert.Null(persistedEvent.AppliedPrice);
        Assert.Null(persistedEvent.ProcessedAtUtc);
        Assert.Equal(100m, persistedPrice);
        Assert.Equal(1, updater.CallCount);
    }

    [Fact]
    public async Task ExplicitTransactionRollback_RestoresPendingEventAndOriginalPrice()
    {
        var scenario = await SeedScenarioAsync();

        await using (var transactionContext =
            _database.CreateDbContext())
        await using (var transaction =
            await transactionContext.Database.BeginTransactionAsync())
        {
            var (product, repricingEvent) = await LoadScenarioAsync(
                transactionContext,
                scenario);

            repricingEvent.ApproveAutomatically(
                "Transaction rollback integration test.");
            product.CurrentPrice = 98m;

            await transactionContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verificationContext =
            _database.CreateDbContext();

        var persistedStatus = await verificationContext.RepricingEvents
            .AsNoTracking()
            .Where(x => x.Id == scenario.RepricingEventId)
            .Select(x => x.Status)
            .SingleAsync();

        var persistedPrice = await verificationContext.Products
            .AsNoTracking()
            .Where(x => x.Id == scenario.ProductId)
            .Select(x => x.CurrentPrice)
            .SingleAsync();

        Assert.Equal(RepricingStatus.Pending, persistedStatus);
        Assert.Equal(100m, persistedPrice);
    }

    private async Task<ScenarioIds> SeedScenarioAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");

        var store = new AmazonStore
        {
            Name = $"Integration Store {suffix}",
            SellerId = $"SELLER-{suffix}",
            MarketplaceId = "A33AVAJ2PDY3EV",
            IsActive = true
        };

        var product = new Product
        {
            AmazonStoreId = store.Id,
            AmazonStore = store,
            Sku = $"SKU-{suffix}",
            Asin = "B0INTEGRATION",
            Title = "PostgreSQL integration test product",
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
            Reason = "PostgreSQL integration test decision."
        };

        await using var dbContext = _database.CreateDbContext();
        dbContext.Products.Add(product);
        dbContext.RepricingEvents.Add(repricingEvent);
        await dbContext.SaveChangesAsync();

        return new ScenarioIds(product.Id, repricingEvent.Id);
    }

    private static async Task<(Product Product, RepricingEvent Event)>
        LoadScenarioAsync(
            RepricerDbContext dbContext,
            ScenarioIds scenario)
    {
        var product = await dbContext.Products
            .Include(x => x.AmazonStore)
            .SingleAsync(x => x.Id == scenario.ProductId);

        var repricingEvent = await dbContext.RepricingEvents
            .SingleAsync(x => x.Id == scenario.RepricingEventId);

        return (product, repricingEvent);
    }

    private async Task AssertFailedStateAsync(
        ScenarioIds scenario,
        string expectedError)
    {
        await using var verificationContext =
            _database.CreateDbContext();

        var persistedEvent = await verificationContext.RepricingEvents
            .AsNoTracking()
            .SingleAsync(x => x.Id == scenario.RepricingEventId);

        var persistedPrice = await verificationContext.Products
            .AsNoTracking()
            .Where(x => x.Id == scenario.ProductId)
            .Select(x => x.CurrentPrice)
            .SingleAsync();

        Assert.Equal(RepricingStatus.Failed, persistedEvent.Status);
        Assert.False(persistedEvent.WasApplied);
        Assert.Null(persistedEvent.AppliedPrice);
        Assert.NotNull(persistedEvent.ProcessedAtUtc);
        Assert.Contains(expectedError, persistedEvent.ApplicationError);
        Assert.Equal(100m, persistedPrice);
    }

    private static AutomaticRepricingExecutor CreateExecutor(
        RepricerDbContext dbContext,
        IAmazonPriceUpdater updater)
    {
        var options = Options.Create(new WorkerOptions
        {
            ExecutionMode = RepricingExecutionMode.Automatic,
            MaxPriceChangePercentage = 10m,
            MinimumRepricingIntervalSeconds = 0
        });

        return new AutomaticRepricingExecutor(
            dbContext,
            updater,
            new AllowAllRepricingGuard(),
            NullLogger<AutomaticRepricingExecutor>.Instance,
            options);
    }

    private static AmazonPriceUpdateResult Accepted(string submissionId) =>
        new(true, submissionId, Array.Empty<string>());

    private sealed record ScenarioIds(
        Guid ProductId,
        Guid RepricingEventId);

    private sealed class AllowAllRepricingGuard
        : IAutomaticRepricingGuard
    {
        public RepricingGuardResult Evaluate(
            decimal currentPrice,
            decimal proposedPrice,
            DateTimeOffset nowUtc,
            DateTimeOffset? lastRepricedAtUtc) =>
            RepricingGuardResult.Allow();
    }

    private sealed class DelegatingAmazonPriceUpdater
        : IAmazonPriceUpdater
    {
        private readonly Func<Task<AmazonPriceUpdateResult>> _update;

        public DelegatingAmazonPriceUpdater(
            Func<Task<AmazonPriceUpdateResult>> update)
        {
            _update = update;
        }

        public int CallCount { get; private set; }

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
            return _update();
        }
    }

    private sealed class FailAppliedPersistenceInterceptor
        : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            var appliedEvent = eventData.Context?.ChangeTracker
                .Entries<RepricingEvent>()
                .Any(x =>
                    x.State == EntityState.Modified &&
                    x.Entity.Status == RepricingStatus.Applied) == true;

            if (appliedEvent)
            {
                throw new SimulatedPersistenceException();
            }

            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }
    }

    private sealed class SimulatedPersistenceException : Exception
    {
        public SimulatedPersistenceException()
            : base("Simulated final PostgreSQL persistence failure.")
        {
        }
    }
}
