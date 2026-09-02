using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;
using AmazonRepricer.IntegrationTests.PostgreSql;
using AmazonRepricer.Worker.Repricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmazonRepricer.IntegrationTests.Worker.Repricing;

[Collection(PostgreSqlCollection.Name)]
public sealed class RepricingReconciliationServicePostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public RepricingReconciliationServicePostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task AcceptedSubmission_FinalizesEventAndUpdatesProductPrice()
    {
        var scenario = await SeedIncompleteSubmissionAsync(
            accepted: true,
            submissionId: "submission-reconcile-accepted",
            issues: Array.Empty<string>());

        int reconciledCount;

        await using (var reconciliationContext =
            _database.CreateDbContext())
        {
            var service = new RepricingReconciliationService(
                reconciliationContext,
                NullLogger<RepricingReconciliationService>.Instance);

            reconciledCount = await service.ReconcileAsync(
                batchSize: 100);
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

        Assert.True(reconciledCount >= 1);
        Assert.Equal(RepricingStatus.Applied, persistedEvent.Status);
        Assert.True(persistedEvent.WasApplied);
        Assert.Equal(99m, persistedEvent.AppliedPrice);
        Assert.Equal(
            "submission-reconcile-accepted",
            persistedEvent.AmazonSubmissionId);
        Assert.NotNull(persistedEvent.ReconciledAtUtc);
        Assert.Equal(99m, persistedPrice);

        await using var secondContext = _database.CreateDbContext();

        var secondService = new RepricingReconciliationService(
            secondContext,
            NullLogger<RepricingReconciliationService>.Instance);

        var secondPassCount = await secondService.ReconcileAsync(
            batchSize: 100);

        Assert.Equal(0, secondPassCount);
    }

    [Fact]
    public async Task RejectedSubmission_FailsEventAndKeepsProductPrice()
    {
        var scenario = await SeedIncompleteSubmissionAsync(
            accepted: false,
            submissionId: "submission-reconcile-rejected",
            issues: new[]
            {
                "ERROR: INVALID_PRICE - Amazon rejected the price."
            });

        await using (var reconciliationContext =
            _database.CreateDbContext())
        {
            var service = new RepricingReconciliationService(
                reconciliationContext,
                NullLogger<RepricingReconciliationService>.Instance);

            var reconciledCount = await service.ReconcileAsync(
                batchSize: 100);

            Assert.True(reconciledCount >= 1);
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

        Assert.Equal(RepricingStatus.Failed, persistedEvent.Status);
        Assert.False(persistedEvent.WasApplied);
        Assert.Null(persistedEvent.AppliedPrice);
        Assert.Contains(
            "Amazon rejected the price",
            persistedEvent.ApplicationError);
        Assert.NotNull(persistedEvent.ReconciledAtUtc);
        Assert.Equal(100m, persistedPrice);
    }

    private async Task<ScenarioIds> SeedIncompleteSubmissionAsync(
        bool accepted,
        string submissionId,
        IReadOnlyCollection<string> issues)
    {
        var suffix = Guid.NewGuid().ToString("N");

        var store = new AmazonStore
        {
            Name = $"Reconciliation Store {suffix}",
            SellerId = $"SELLER-{suffix}",
            MarketplaceId = "A33AVAJ2PDY3EV",
            IsActive = true
        };

        var product = new Product
        {
            AmazonStoreId = store.Id,
            AmazonStore = store,
            Sku = $"RECONCILE-SKU-{suffix}",
            Asin = "B0RECONCILE",
            Title = "Reconciliation integration test product",
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
            Reason = "Reconciliation integration test decision."
        };

        repricingEvent.Approve(
            "Approved before simulated final persistence failure.");

        repricingEvent.RecordAmazonSubmission(
            accepted,
            submissionId,
            issues);

        await using var dbContext = _database.CreateDbContext();

        dbContext.Products.Add(product);
        dbContext.RepricingEvents.Add(repricingEvent);
        await dbContext.SaveChangesAsync();

        return new ScenarioIds(product.Id, repricingEvent.Id);
    }

    private sealed record ScenarioIds(
        Guid ProductId,
        Guid RepricingEventId);
}
