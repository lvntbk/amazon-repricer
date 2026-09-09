using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;
using AmazonRepricer.IntegrationTests.PostgreSql;
using AmazonRepricer.Worker.Repricing;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.IntegrationTests.Worker.Repricing;

[Collection(PostgreSqlCollection.Name)]
public sealed class AutomaticRepricingEligibilityQueryPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public AutomaticRepricingEligibilityQueryPostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task DisabledStore_IsExcludedFromAutomaticRepricingCandidates()
    {
        var suffix = Guid.NewGuid().ToString("N");

        var enabledProduct = CreateProduct(
            suffix + "-enabled",
            automaticRepricingEnabled: true);

        var disabledProduct = CreateProduct(
            suffix + "-disabled",
            automaticRepricingEnabled: false);

        await using var dbContext =
            _database.CreateDbContext();

        dbContext.Products.AddRange(
            enabledProduct,
            disabledProduct);

        await dbContext.SaveChangesAsync();

        var candidateIds =
            await dbContext.Products
                .AsNoTracking()
                .EligibleForAutomaticRepricing()
                .Select(x => x.Id)
                .ToListAsync();

        Assert.Contains(
            enabledProduct.Id,
            candidateIds);

        Assert.DoesNotContain(
            disabledProduct.Id,
            candidateIds);
    }

    private static Product CreateProduct(
        string suffix,
        bool automaticRepricingEnabled)
    {
        var store = new AmazonStore
        {
            Name = $"Eligibility Store {suffix}",
            SellerId = $"SELLER-ELIGIBILITY-{suffix}",
            MarketplaceId = "A33AVAJ2PDY3EV",
            IsActive = true,
            AutomaticRepricingEnabled =
                automaticRepricingEnabled
        };

        var product = new Product
        {
            AmazonStoreId = store.Id,
            AmazonStore = store,
            Sku = $"SKU-ELIGIBILITY-{suffix}",
            Asin = "B0ELIGIBILITY",
            Title = "Eligibility integration test product",
            ProductType = "PRODUCT",
            CurrencyCode = "TRY",
            CurrentPrice = 100m,
            IsRepricingEnabled = true
        };

        var rule = new PricingRule
        {
            ProductId = product.Id,
            Product = product,
            Strategy = PricingStrategy.MatchFeaturedOffer,
            MinimumPrice = 90m,
            MaximumPrice = 110m,
            AdjustmentValue = 0m,
            IsActive = true
        };

        product.PricingRule = rule;

        return product;
    }
}
