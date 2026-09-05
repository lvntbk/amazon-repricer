using AmazonRepricer.Api.Contracts.RepricingEvents;
using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Domain.Enums;
using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Api.Controllers;

[ApiController]
[Route("api/repricing-events")]
public sealed class RepricingEventsController : ControllerBase
{
    private readonly RepricerDbContext _dbContext;
    private readonly IAmazonPriceUpdater _priceUpdater;
    private readonly AmazonSpApiOptions _amazonOptions;
    private readonly ILogger<RepricingEventsController> _logger;

    public RepricingEventsController(
        RepricerDbContext dbContext,
        IAmazonPriceUpdater priceUpdater,
        IOptions<AmazonSpApiOptions> amazonOptions,
        ILogger<RepricingEventsController> logger)
    {
        _dbContext = dbContext;
        _priceUpdater = priceUpdater;
        _amazonOptions = amazonOptions.Value;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] RepricingStatus? status,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.RepricingEvents
            .AsNoTracking()
            .Include(x => x.Product)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        var events = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .Select(x => new
            {
                x.Id,
                x.ProductId,
                x.Product.Sku,
                x.Product.Asin,
                x.OldPrice,
                x.ProposedPrice,
                x.AppliedPrice,
                x.Reason,
                x.WasApplied,
                Status = x.Status.ToString(),
                x.ReviewNote,
                x.ReviewedAtUtc,
                x.ProcessedAtUtc,
                x.ApplicationError,
                x.AmazonSubmissionId,
                x.AmazonSubmissionAccepted,
                x.AmazonSubmissionIssues,
                x.SubmittedAtUtc,
                x.ReconciledAtUtc,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(events);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await _dbContext.RepricingEvents
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.ProductId,
                x.OldPrice,
                x.ProposedPrice,
                x.AppliedPrice,
                x.Reason,
                x.WasApplied,
                Status = x.Status.ToString(),
                x.ReviewNote,
                x.ReviewedAtUtc,
                x.ProcessedAtUtc,
                x.ApplicationError,
                x.AmazonSubmissionId,
                x.AmazonSubmissionAccepted,
                x.AmazonSubmissionIssues,
                x.SubmittedAtUtc,
                x.ReconciledAtUtc,
                x.CreatedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id,
        ReviewRepricingEventRequest request,
        CancellationToken cancellationToken)
    {
        return await Review(
            id,
            request,
            approve: true,
            cancellationToken);
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid id,
        ReviewRepricingEventRequest request,
        CancellationToken cancellationToken)
    {
        return await Review(
            id,
            request,
            approve: false,
            cancellationToken);
    }

    [HttpPost("{id:guid}/apply")]
    public async Task<IActionResult> Apply(
        Guid id,
        CancellationToken cancellationToken)
    {
        var repricingEvent =
            await _dbContext.RepricingEvents
                .Include(x => x.Product)
                .ThenInclude(x => x.AmazonStore)
                .Include(x => x.Product)
                .ThenInclude(x => x.PricingRule)
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);

        if (repricingEvent is null)
            return NotFound();

        if (repricingEvent.Status != RepricingStatus.Approved)
        {
            return Conflict(
                $"Only approved events can be applied. " +
                $"Current status: {repricingEvent.Status}.");
        }

        var product = repricingEvent.Product;
        var amazonStore = product.AmazonStore;

        if (!amazonStore.IsActive)
        {
            return Conflict(
                "The Amazon store is not active.");
        }

        if (!product.IsRepricingEnabled)
        {
            return Conflict(
                "Repricing is disabled for this product.");
        }

        if (!product.CurrentPrice.HasValue)
        {
            return Conflict(
                "Product does not have a current price.");
        }

        if (product.CurrentPrice.Value !=
            repricingEvent.OldPrice)
        {
            return Conflict(new
            {
                error =
                    "The approved event is based on a stale price.",
                currentPrice = product.CurrentPrice.Value,
                eventOldPrice = repricingEvent.OldPrice
            });
        }

        var safetyResult =
            PriceSubmissionSafetyPolicy.EvaluateHardBounds(
                product.CurrentPrice.Value,
                repricingEvent.ProposedPrice,
                product.PricingRule);

        if (!safetyResult.IsAllowed)
        {
            _logger.LogWarning(
                "Manual repricing blocked by safety policy for " +
                "event {RepricingEventId}, SKU {Sku}: {Reason}",
                repricingEvent.Id,
                product.Sku,
                safetyResult.Reason);

            return Conflict(new
            {
                error = "Manual repricing blocked by safety policy.",
                reason = safetyResult.Reason
            });
        }

        if (_dbContext.Database.IsRelational())
        {
            var claimedRowCount = await _dbContext.RepricingEvents
                .Where(x =>
                    x.Id == repricingEvent.Id &&
                    x.Status == RepricingStatus.Approved)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        x => x.Status,
                        RepricingStatus.Applying),
                    cancellationToken);

            // ExecuteUpdate bypasses EF Core's change tracker.
            await _dbContext.Entry(repricingEvent)
                .ReloadAsync(cancellationToken);

            if (claimedRowCount == 0)
            {
                _logger.LogInformation(
                    "Manual apply claim rejected for repricing event " +
                    "{RepricingEventId}. Current status: {Status}.",
                    repricingEvent.Id,
                    repricingEvent.Status);

                return Conflict(new
                {
                    error =
                        "Repricing event was already claimed or processed.",
                    currentStatus =
                        repricingEvent.Status.ToString()
                });
            }
        }
        else
        {
            // EF Core in-memory does not support ExecuteUpdateAsync.
            try
            {
                repricingEvent.BeginApplication();
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                return Conflict(exception.Message);
            }
        }

        AmazonPriceUpdateResult updateResult;

        try
        {
            updateResult =
                await _priceUpdater.UpdatePriceAsync(
                    amazonStore.SellerId,
                    product.Sku,
                    amazonStore.MarketplaceId,
                    _amazonOptions.DefaultProductType,
                    repricingEvent.ProposedPrice,
                    _amazonOptions.CurrencyCode,
                    cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Amazon price submission failed for " +
                "repricing event {RepricingEventId}, SKU {Sku}.",
                repricingEvent.Id,
                product.Sku);

            return StatusCode(
                StatusCodes.Status502BadGateway,
                "Amazon price service could not be reached " +
                "or returned an HTTP error.");
        }

        repricingEvent.RecordAmazonSubmission(
            updateResult.Accepted,
            updateResult.SubmissionId,
            updateResult.Issues);

        // Persist the external result before finalizing local state.
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!updateResult.Accepted)
        {
            var applicationError =
                updateResult.Issues.Count == 0
                    ? "Amazon did not accept the price update."
                    : string.Join(
                        " | ",
                        updateResult.Issues);

            if (applicationError.Length > 1000)
                applicationError = applicationError[..1000];

            repricingEvent.MarkFailed(applicationError);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return UnprocessableEntity(new
            {
                repricingEvent.Id,
                Status = repricingEvent.Status.ToString(),
                SubmissionId = repricingEvent.AmazonSubmissionId,
                SubmissionAccepted =
                    repricingEvent.AmazonSubmissionAccepted,
                Issues = repricingEvent.AmazonSubmissionIssues,
                repricingEvent.SubmittedAtUtc,
                repricingEvent.ProcessedAtUtc
            });
        }

        repricingEvent.MarkApplied(
            repricingEvent.ProposedPrice);

        product.CurrentPrice =
            repricingEvent.ProposedPrice;

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return Ok(new
        {
            EventId = repricingEvent.Id,
            ProductId = product.Id,
            product.Sku,
            OldPrice = repricingEvent.OldPrice,
            NewPrice = product.CurrentPrice,
            Status = repricingEvent.Status.ToString(),
            SubmissionId = repricingEvent.AmazonSubmissionId,
            SubmissionAccepted =
                repricingEvent.AmazonSubmissionAccepted,
            repricingEvent.SubmittedAtUtc,
            repricingEvent.ProcessedAtUtc
        });
    }

    private async Task<IActionResult> Review(
        Guid id,
        ReviewRepricingEventRequest request,
        bool approve,
        CancellationToken cancellationToken)
    {
        if (request.Note?.Length > 1000)
        {
            return BadRequest(
                "Review note cannot exceed 1000 characters.");
        }

        var repricingEvent =
            await _dbContext.RepricingEvents.FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);

        if (repricingEvent is null)
            return NotFound();

        try
        {
            if (approve)
                repricingEvent.Approve(request.Note);
            else
                repricingEvent.Reject(request.Note);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(exception.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            repricingEvent.Id,
            Status = repricingEvent.Status.ToString(),
            repricingEvent.ReviewNote,
            repricingEvent.ReviewedAtUtc
        });
    }
}
