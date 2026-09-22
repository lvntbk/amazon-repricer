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
    private readonly WorkerOptions _workerOptions;

    public RepricingVerificationService(
        IServiceScopeFactory scopeFactory,
        ILogger<RepricingVerificationService> logger,
        IOptions<AmazonSpApiOptions> amazonOptions,
        IOptions<WorkerOptions> workerOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _amazonOptions = amazonOptions.Value;
        _workerOptions = workerOptions.Value;

        var validation = new WorkerOptionsValidator()
            .Validate(null, _workerOptions);

        if (validation.Failed)
        {
            throw new InvalidOperationException(
                string.Join("; ", validation.Failures));
        }
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

            var now = DateTime.UtcNow;

            var waiting = db.RepricingEvents.Where(x =>
                x.Status == RepricingStatus.AwaitingVerification &&
                x.AmazonSubmissionAccepted == true &&
                x.SubmittedAtUtc != null &&
                !x.VerificationReviewRequired &&
                x.Product.AmazonStore.SellerId == _amazonOptions.SellerId &&
                x.Product.AmazonStore.MarketplaceId == _amazonOptions.MarketplaceId);

            // Recover exhausted attempts whose owner crashed or disappeared.
            await waiting
                .Where(x =>
                    x.VerificationAttemptCount >=
                        _workerOptions.VerificationMaximumAttempts &&
                    (x.VerificationLeaseExpiresAtUtc == null ||
                     x.VerificationLeaseExpiresAtUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.VerificationReviewRequired, true)
                    .SetProperty(x => x.NextVerificationAttemptAtUtc, (DateTime?)null)
                    .SetProperty(x => x.VerificationLeaseId, (Guid?)null)
                    .SetProperty(x => x.VerificationLeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(x => x.LastVerificationReason,
                        "Verification attempt limit reached; review required."),
                    cancellationToken);

            ids = await waiting
                .AsNoTracking()
                .Where(x =>
                    x.VerificationAttemptCount <
                        _workerOptions.VerificationMaximumAttempts &&
                    (x.NextVerificationAttemptAtUtc == null ||
                     x.NextVerificationAttemptAtUtc <= now) &&
                    (x.VerificationLeaseExpiresAtUtc == null ||
                     x.VerificationLeaseExpiresAtUtc <= now))
                .OrderBy(x => x.NextVerificationAttemptAtUtc ?? x.SubmittedAtUtc)
                .ThenBy(x => x.Id)
                .Take(batchSize)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        }

        var verifiedCount = 0;

        foreach (var id in ids)
        {
            var leaseId = Guid.NewGuid();

            try
            {
                if (await VerifyOneAsync(id, leaseId, cancellationToken))
                {
                    verifiedCount++;
                }
                else
                {
                    await ScheduleRetryAsync(
                        id, leaseId,
                        "Price verification was inconclusive; see verification logs.",
                        cancellationToken);
                }
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

                try
                {
                    await ScheduleRetryAsync(
                        id, leaseId,
                        "Verification attempt failed: " + exception.GetType().Name,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception schedulingException)
                {
                    _logger.LogError(
                        schedulingException,
                        "Could not schedule event {EventId}; "
                        + "the persisted lease will expire.",
                        id);
                }
            }
        }

        return verifiedCount;
    }

    private async Task ScheduleRetryAsync(
        Guid id,
        Guid leaseId,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RepricerDbContext>();

        var item = await db.RepricingEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == id &&
                x.Status == RepricingStatus.AwaitingVerification &&
                x.VerificationLeaseId == leaseId,
                cancellationToken);

        if (item is null)
            return;

        var reviewRequired = item.VerificationAttemptCount >=
            _workerOptions.VerificationMaximumAttempts;

        var delaySeconds = _workerOptions.VerificationInitialDelaySeconds;

        for (var attempt = 1;
             attempt < item.VerificationAttemptCount &&
             delaySeconds < _workerOptions.VerificationMaximumDelaySeconds;
             attempt++)
        {
            delaySeconds = Math.Min(
                delaySeconds * 2,
                _workerOptions.VerificationMaximumDelaySeconds);
        }

        DateTime? nextAttempt = reviewRequired
            ? null
            : DateTime.UtcNow.AddSeconds(delaySeconds);

        var recordedReason = reviewRequired
            ? "Review required after attempt limit. " + reason
            : reason;

        if (recordedReason.Length > 1000)
            recordedReason = recordedReason[..1000];

        // The token prevents an expired owner from updating a new owner's work.
        var updated = await db.RepricingEvents
            .Where(x =>
                x.Id == id &&
                x.Status == RepricingStatus.AwaitingVerification &&
                x.VerificationLeaseId == leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.VerificationReviewRequired, reviewRequired)
                .SetProperty(x => x.NextVerificationAttemptAtUtc, nextAttempt)
                .SetProperty(x => x.LastVerificationReason, recordedReason)
                .SetProperty(x => x.VerificationLeaseId, (Guid?)null)
                .SetProperty(x => x.VerificationLeaseExpiresAtUtc, (DateTime?)null),
                cancellationToken);

        if (updated == 1 && reviewRequired)
        {
            _logger.LogWarning(
                "Event {EventId} requires review after {AttemptCount} attempts.",
                id,
                item.VerificationAttemptCount);
        }
    }

    private async Task<bool> VerifyOneAsync(
        Guid id,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<RepricerDbContext>();
        var reader = scope.ServiceProvider
            .GetRequiredService<IAmazonListingReader>();

        var claimTime = DateTime.UtcNow;
        var leaseExpiry = claimTime.AddSeconds(
            _workerOptions.VerificationLeaseSeconds);

        var claimed = await db.RepricingEvents
            .Where(x =>
                x.Id == id &&
                x.Status == RepricingStatus.AwaitingVerification &&
                x.AmazonSubmissionAccepted == true &&
                x.SubmittedAtUtc != null &&
                !x.VerificationReviewRequired &&
                x.Product.AmazonStore.SellerId == _amazonOptions.SellerId &&
                x.Product.AmazonStore.MarketplaceId == _amazonOptions.MarketplaceId &&
                x.VerificationAttemptCount <
                    _workerOptions.VerificationMaximumAttempts &&
                (x.NextVerificationAttemptAtUtc == null ||
                 x.NextVerificationAttemptAtUtc <= claimTime) &&
                (x.VerificationLeaseExpiresAtUtc == null ||
                 x.VerificationLeaseExpiresAtUtc <= claimTime))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.VerificationLeaseId, (Guid?)leaseId)
                .SetProperty(x => x.VerificationLeaseExpiresAtUtc, (DateTime?)leaseExpiry)
                .SetProperty(x => x.LastVerificationAttemptAtUtc, (DateTime?)claimTime)
                .SetProperty(x => x.NextVerificationAttemptAtUtc, (DateTime?)leaseExpiry)
                .SetProperty(x => x.VerificationAttemptCount,
                    x => x.VerificationAttemptCount + 1)
                .SetProperty(x => x.LastVerificationReason,
                    "Verification attempt started."),
                cancellationToken);

        if (claimed != 1)
            return false;

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
            current.VerificationLeaseId != leaseId ||
            current.VerificationLeaseExpiresAtUtc == null ||
            current.VerificationLeaseExpiresAtUtc <= DateTime.UtcNow ||
            current.VerificationReviewRequired ||
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
            TimeSpan.FromSeconds(
                _workerOptions.VerificationMaximumObservationAgeSeconds));

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
        current.VerificationLeaseId = null;
        current.VerificationLeaseExpiresAtUtc = null;
        current.NextVerificationAttemptAtUtc = null;
        current.LastVerificationReason = result.Reason;

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
