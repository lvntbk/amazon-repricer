using AmazonRepricer.Api.Contracts.RepricingEvents;
using AmazonRepricer.Domain.Enums;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Api.Controllers;

[ApiController]
[Route("api/repricing-events")]
public sealed class RepricingEventsController : ControllerBase
{
    private readonly RepricerDbContext _dbContext;

    public RepricingEventsController(
        RepricerDbContext dbContext)
    {
        _dbContext = dbContext;
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
