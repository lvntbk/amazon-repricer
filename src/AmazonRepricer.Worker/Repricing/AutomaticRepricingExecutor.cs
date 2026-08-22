using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Domain.Entities;
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
    private readonly IAutomaticRepricingGuard _guard;
    private readonly ILogger<AutomaticRepricingExecutor> _logger;
    private readonly WorkerOptions _options;

    public AutomaticRepricingExecutor(
        RepricerDbContext dbContext,
        IAmazonPriceUpdater amazonPriceUpdater,
        IAutomaticRepricingGuard guard,
        ILogger<AutomaticRepricingExecutor> logger,
        IOptions<WorkerOptions> options)
    {
        _dbContext = dbContext;
        _amazonPriceUpdater = amazonPriceUpdater;
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

        repricingEvent.ApproveAutomatically(
            $"Automatic repricing approved. {guardResult.Reason}");

        // Persist the approved pricing intent before the external side effect.
        await _dbContext.SaveChangesAsync(cancellationToken);

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
