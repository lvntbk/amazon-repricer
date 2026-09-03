using AmazonRepricer.Domain.Enums;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Worker.Repricing;

public sealed class RepricingReconciliationService
    : IRepricingReconciliationService
{
    private readonly RepricerDbContext _dbContext;
    private readonly ILogger<RepricingReconciliationService> _logger;

    public RepricingReconciliationService(
        RepricerDbContext dbContext,
        ILogger<RepricingReconciliationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<int> ReconcileAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "Reconciliation batch size must be greater than zero.");
        }

        var candidates = await _dbContext.RepricingEvents
            .Include(x => x.Product)
            .Where(x =>
                (x.Status == RepricingStatus.Approved ||
                    x.Status == RepricingStatus.Applying) &&
                x.AmazonSubmissionAccepted != null &&
                x.SubmittedAtUtc != null &&
                x.ReconciledAtUtc == null)
            .OrderBy(x => x.SubmittedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var repricingEvent in candidates)
        {
            if (repricingEvent.AmazonSubmissionAccepted == true)
            {
                repricingEvent.MarkApplied(
                    repricingEvent.ProposedPrice);

                repricingEvent.Product.CurrentPrice =
                    repricingEvent.ProposedPrice;
            }
            else
            {
                var error =
                    string.IsNullOrWhiteSpace(
                        repricingEvent.AmazonSubmissionIssues)
                        ? "Amazon rejected the price update."
                        : repricingEvent.AmazonSubmissionIssues;

                if (error.Length > 1000)
                {
                    error = error[..1000];
                }

                repricingEvent.MarkFailed(error);
            }

            repricingEvent.MarkReconciled();

            _logger.LogWarning(
                "Reconciled repricing event {RepricingEventId}. SubmissionId: {SubmissionId}, accepted: {Accepted}, final status: {Status}.",
                repricingEvent.Id,
                repricingEvent.AmazonSubmissionId,
                repricingEvent.AmazonSubmissionAccepted,
                repricingEvent.Status);
        }

        if (candidates.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return candidates.Count;
    }
}
