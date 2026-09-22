using System.Data;
using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Domain.Enums;
using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Worker.Repricing;

public sealed class RepricingVerificationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RepricingVerificationService> _logger;
    private readonly AmazonSpApiOptions _amazonOptions;

    public RepricingVerificationService(
        IServiceScopeFactory scopeFactory,
        ILogger<RepricingVerificationService> logger,
        IOptions<AmazonSpApiOptions> amazonOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _amazonOptions = amazonOptions.Value;
    }

    public async Task<int> VerifyAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        // Never read real Amazon data while mock mode is selected.
        if (_amazonOptions.UseMock)
            return 0;

        List<Guid> ids;

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<RepricerDbContext>();

            ids = await db.RepricingEvents
                .AsNoTracking()
                .Where(x =>
                    x.Status == RepricingStatus.AwaitingVerification &&
                    x.AmazonSubmissionAccepted == true &&
                    x.SubmittedAtUtc != null)
                .OrderBy(x => x.SubmittedAtUtc)
                .ThenBy(x => x.Id)
                .Take(batchSize)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        }

        var verifiedCount = 0;

        foreach (var id in ids)
        {
            try
            {
                if (await VerifyOneAsync(id, cancellationToken))
                    verifiedCount++;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A failed read or transaction must not mark a price applied.
                _logger.LogError(
                    exception,
                    "Price verification failed for event {EventId}; "
                    + "no successful completion was recorded by this attempt.",
                    id);
            }
        }

        return verifiedCount;
    }

    private async Task<bool> VerifyOneAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<RepricerDbContext>();
        var reader = scope.ServiceProvider
            .GetRequiredService<IAmazonListingReader>();

        var initial = await db.RepricingEvents
            .AsNoTracking()
            .Include(x => x.Product)
            .ThenInclude(x => x.AmazonStore)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (initial is null ||
            initial.Status != RepricingStatus.AwaitingVerification ||
            initial.AmazonSubmissionAccepted != true ||
            initial.SubmittedAtUtc is null)
        {
            return false;
        }

        var product = initial.Product;
        var store = product.AmazonStore;

        // The current token provider represents one configured seller.
        if (!string.Equals(store.SellerId, _amazonOptions.SellerId,
                StringComparison.Ordinal) ||
            !string.Equals(store.MarketplaceId, _amazonOptions.MarketplaceId,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(product.CurrencyCode))
        {
            _logger.LogWarning(
                "Verification blocked for event {EventId}: "
                + "seller, marketplace or currency configuration is incompatible.",
                id);
            return false;
        }

        var expectation = new ListingPriceExpectation(
            store.SellerId,
            product.Sku,
            store.MarketplaceId,
            product.CurrencyCode,
            initial.ProposedPrice,
            new DateTimeOffset(
                DateTime.SpecifyKind(
                    initial.SubmittedAtUtc.Value,
                    DateTimeKind.Utc)));

        // Network I/O happens outside the database transaction.
        var observation = await reader.GetListingAsync(
            expectation.SellerId,
            expectation.Sku,
            expectation.MarketplaceId,
            cancellationToken);

        await using var transaction = await db.Database
            .BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var current = await db.RepricingEvents
            .Include(x => x.Product)
            .ThenInclude(x => x.AmazonStore)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (current is null ||
            current.Status != RepricingStatus.AwaitingVerification ||
            current.AmazonSubmissionAccepted != true ||
            current.SubmittedAtUtc != initial.SubmittedAtUtc ||
            current.AmazonSubmissionId != initial.AmazonSubmissionId ||
            current.ProductId != initial.ProductId ||
            current.OldPrice != initial.OldPrice ||
            current.ProposedPrice != expectation.ProposedPrice ||
            current.Product.CurrentPrice != initial.OldPrice ||
            current.Product.Sku != expectation.Sku ||
            current.Product.CurrencyCode != expectation.CurrencyCode ||
            current.Product.AmazonStore.SellerId != expectation.SellerId ||
            current.Product.AmazonStore.MarketplaceId != expectation.MarketplaceId)
        {
            return false;
        }

        // Conservatively avoid overwriting a competing/newer decision.
        var conflictingEvent = await db.RepricingEvents
            .AnyAsync(x =>
                x.ProductId == current.ProductId &&
                x.Id != current.Id &&
                (x.CreatedAtUtc >= current.CreatedAtUtc ||
                 x.Status == RepricingStatus.Applying ||
                 x.Status == RepricingStatus.AwaitingVerification),
                cancellationToken);

        if (conflictingEvent)
        {
            _logger.LogWarning(
                "Verification completion blocked for event {EventId}: "
                + "another product decision requires reconciliation.",
                id);
            return false;
        }

        var result = ListingPriceVerificationPolicy.Evaluate(
            expectation,
            observation,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(2));

        if (!result.IsVerified || result.VerifiedPrice is null)
        {
            _logger.LogInformation(
                "Event {EventId} remains awaiting verification: {Reason}",
                id,
                result.Reason);
            return false;
        }

        current.MarkApplied(result.VerifiedPrice.Value);
        current.MarkReconciled();
        current.Product.CurrentPrice = result.VerifiedPrice.Value;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Amazon price verified for event {EventId}, SKU {Sku}: {Price}.",
            id,
            current.Product.Sku,
            result.VerifiedPrice.Value);

        return true;
    }
}
