using AmazonRepricer.Api.Contracts.Repricing;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Api.Controllers;

[ApiController]
[Route("api/repricing")]
public sealed class RepricingController : ControllerBase
{
    private readonly RepricerDbContext _dbContext;
    private readonly IPricingEngine _pricingEngine;

    public RepricingController(
        RepricerDbContext dbContext,
        IPricingEngine pricingEngine)
    {
        _dbContext = dbContext;
        _pricingEngine = pricingEngine;
    }

    [HttpPost("evaluate")]
    public async Task<ActionResult> Evaluate(
        EvaluateRepricingRequest request,
        CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .Include(x => x.PricingRule)
            .FirstOrDefaultAsync(
                x => x.Id == request.ProductId,
                cancellationToken);

        if (product is null)
            return NotFound("Product does not exist.");

        if (!product.IsRepricingEnabled)
            return BadRequest("Repricing is disabled for this product.");

        if (product.PricingRule is null)
            return BadRequest("Product does not have a pricing rule.");

        if (product.CurrentPrice is null)
            return BadRequest("Product does not have a current price.");

        var result = _pricingEngine.Calculate(
            product.CurrentPrice.Value,
            request.FeaturedOfferPrice,
            request.IsFeaturedOfferOurs,
            product.PricingRule);

        var repricingEvent = new RepricingEvent
        {
            ProductId = product.Id,
            OldPrice = product.CurrentPrice.Value,
            ProposedPrice = result.ProposedPrice,
            AppliedPrice = null,
            Reason = result.Reason,
            WasApplied = false
        };

        _dbContext.RepricingEvents.Add(repricingEvent);

        var snapshot = new PriceSnapshot
        {
            ProductId = product.Id,
            OurPrice = product.CurrentPrice.Value,
            FeaturedOfferPrice = request.FeaturedOfferPrice,
            IsFeaturedOfferOurs = request.IsFeaturedOfferOurs
        };

        _dbContext.PriceSnapshots.Add(snapshot);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            product.Id,
            product.Sku,
            CurrentPrice = product.CurrentPrice,
            request.FeaturedOfferPrice,
            request.IsFeaturedOfferOurs,
            result.ProposedPrice,
            result.ShouldChangePrice,
            result.Reason,
            EventId = repricingEvent.Id
        });
    }
}
