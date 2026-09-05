using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Worker.Repricing;

public sealed class AutomaticRepricingExecutor
    : IAutomaticRepricingExecutor
{
    private readonly RepricerDbContext _dbContext;
    private readonly IAmazonPriceUpdater _amazonPriceUpdater;
    private readonly IPriceUpdateSafetyGate _priceUpdateSafetyGate;
    private readonly IAutomaticRepricingGuard _guard;
    private readonly ILogger<AutomaticRepricingExecutor> _logger;
    private readonly WorkerOptions _options;

    public AutomaticRepricingExecutor(
        RepricerDbContext dbContext,
        IAmazonPriceUpdater amazonPriceUpdater,
        IPriceUpdateSafetyGate priceUpdateSafetyGate,
        IAutomaticRepricingGuard guard,
        ILogger<AutomaticRepricingExecutor> logger,
        IOptions<WorkerOptions> options)
    {
        _dbContext = dbContext;
        _amazonPriceUpdater = amazonPriceUpdater;
        _priceUpdateSafetyGate = priceUpdateSafetyGate;
        _guard = guard;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<AutomaticRepricingExecutionResult> ExecuteAsync(
        Product product,
        RepricingEvent repricingEvent,
        CancellationToken cancellationToken = default)
    {
        if (_options.ExecutionMode != RepricingExecutionMode.Automatic)
        {
            return AutomaticRepricingExecutionResult.Skipped(
                $"Execution mode is {_options.ExecutionMode}.");
        }

        if (string.IsNullOrWhiteSpace(product.ProductType) ||
            string.IsNullOrWhiteSpace(product.CurrencyCode))
        {
            return AutomaticRepricingExecutionResult.Skipped(
                "Amazon listing metadata is incomplete.");
        }

        var hardSafetyResult =
            PriceSubmissionSafetyPolicy.EvaluateHardBounds(
                product.CurrentPrice
                    ?? repricingEvent.OldPrice,
                repricingEvent.ProposedPrice,
                product.PricingRule);

        if (!hardSafetyResult.IsAllowed)
        {
            _logger.LogWarning(
                "Automatic repricing blocked by hard safety policy " +
                "for event {RepricingEventId}, SKU {Sku}: {Reason}",
                repricingEvent.Id,
                product.Sku,
                hardSafetyResult.Reason);

            return AutomaticRepricingExecutionResult.Skipped(
                $"Automatic repricing blocked: " +
                hardSafetyResult.Reason);
        }

        var priceUpdateGateResult =
            await _priceUpdateSafetyGate.EvaluateAsync(
                cancellationToken);

        if (!priceUpdateGateResult.IsAllowed)
        {
            _logger.LogWarning(
                "Automatic repricing blocked by global safety gate " +
                "for event {RepricingEventId}, SKU {Sku}: {Reason}",
                repricingEvent.Id,
                product.Sku,
                priceUpdateGateResult.Reason);

            return AutomaticRepricingExecutionResult.Skipped(
                $"Automatic repricing blocked: " +
                priceUpdateGateResult.Reason);
        }

        var lastAppliedEvent = await _dbContext.RepricingEvents
            .AsNoTracking()
            .Where(x =>
                x.ProductId == product.Id &&
                x.WasApplied &&
                x.ProcessedAtUtc != null)
            .OrderByDescending(x => x.ProcessedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var lastRepricedAtUtc =
            lastAppliedEvent?.ProcessedAtUtc is DateTime processedAtUtc
                ? new DateTimeOffset(
                    DateTime.SpecifyKind(
                        processedAtUtc,
                        DateTimeKind.Utc))
                : (DateTimeOffset?)null;

        var guardResult = _guard.Evaluate(
            product.CurrentPrice
                ?? repricingEvent.OldPrice,
            repricingEvent.ProposedPrice,
            DateTimeOffset.UtcNow,
            lastRepricedAtUtc);

        if (!guardResult.IsAllowed)
        {
            return AutomaticRepricingExecutionResult.Skipped(
                $"Automatic repricing blocked: {guardResult.Reason}");
        }

        var approvalReason =
            $"Automatic repricing approved. {guardResult.Reason}";

        if (_dbContext.Database.IsRelational())
        {
            // Persist a newly created pending event before claiming it.
            await _dbContext.SaveChangesAsync(cancellationToken);

            var reviewedAtUtc = DateTime.UtcNow;

            var claimedRowCount = await _dbContext.RepricingEvents
                .Where(x =>
                    x.Id == repricingEvent.Id &&
                    x.Status == RepricingStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            x => x.Status,
                            RepricingStatus.Applying)
                        .SetProperty(
                            x => x.ReviewNote,
                            approvalReason)
                        .SetProperty(
                            x => x.ReviewedAtUtc,
                            reviewedAtUtc),
                    cancellationToken);

            await _dbContext.Entry(repricingEvent)
                .ReloadAsync(cancellationToken);

            if (claimedRowCount == 0)
            {
                _logger.LogInformation(
                    "Repricing event {RepricingEventId} was already claimed or processed.",
                    repricingEvent.Id);

                return AutomaticRepricingExecutionResult.Skipped(
                    "Repricing event was already claimed or processed.");
            }
        }
        else
        {
            // EF Core in-memory does not support ExecuteUpdateAsync.
            if (repricingEvent.Status != RepricingStatus.Pending)
            {
                return AutomaticRepricingExecutionResult.Skipped(
                    "Repricing event was already claimed or processed.");
            }

            repricingEvent.ApproveAutomatically(approvalReason);
            repricingEvent.BeginApplication();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        AmazonPriceUpdateResult updateResult;

        try
        {
            updateResult =
                await _amazonPriceUpdater.UpdatePriceAsync(
                    product.AmazonStore.SellerId,
                    product.Sku,
                    product.AmazonStore.MarketplaceId,
                    product.ProductType!,
                    repricingEvent.ProposedPrice,
                    product.CurrencyCode!,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogError(
                exception,
                "Amazon price submission timed out or was canceled " +
                "after dispatch for SKU {Sku}. " +
                "Repricing event {RepricingEventId} remains Applying.",
                product.Sku,
                repricingEvent.Id);

            return AutomaticRepricingExecutionResult.Failed(
                "Amazon price submission outcome is uncertain. " +
                "The repricing event remains Applying to prevent " +
                "duplicate submission.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Amazon price submission outcome is uncertain for SKU {Sku}. " +
                "Repricing event {RepricingEventId} remains Applying.",
                product.Sku,
                repricingEvent.Id);

            return AutomaticRepricingExecutionResult.Failed(
                "Amazon price submission outcome is uncertain. " +
                "The repricing event remains Applying to prevent " +
                "duplicate submission.");
        }
        catch (Exception exception)
        {
            repricingEvent.MarkFailed(
                $"Amazon price update failed before acceptance: " +
                exception.Message);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogError(
                exception,
                "Automatic repricing failed for SKU {Sku}.",
                product.Sku);

            return AutomaticRepricingExecutionResult.Failed(
                repricingEvent.ApplicationError ??
                "Amazon price update failed.");
        }

        repricingEvent.RecordAmazonSubmission(
            updateResult.Accepted,
            updateResult.SubmissionId,
            updateResult.Issues);

        // Persist the external result before finalizing local state.
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!updateResult.Accepted)
        {
            var issues = updateResult.Issues.Count == 0
                ? "Amazon rejected the price update."
                : string.Join("; ", updateResult.Issues);

            repricingEvent.MarkFailed(issues);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Amazon rejected automatic repricing for SKU {Sku}: {Issues}",
                product.Sku,
                issues);

            return AutomaticRepricingExecutionResult.Failed(issues);
        }

        repricingEvent.MarkApplied(repricingEvent.ProposedPrice);
        product.CurrentPrice = repricingEvent.ProposedPrice;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Automatic repricing accepted for SKU {Sku}. Price {OldPrice} -> {NewPrice}. SubmissionId: {SubmissionId}",
            product.Sku,
            repricingEvent.OldPrice,
            repricingEvent.ProposedPrice,
            updateResult.SubmissionId);

        return AutomaticRepricingExecutionResult.Applied();
    }
}
