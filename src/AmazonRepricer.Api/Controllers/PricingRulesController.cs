using AmazonRepricer.Api.Contracts.PricingRules;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Api.Controllers;

[ApiController]
[Route("api/pricing-rules")]
public sealed class PricingRulesController : ControllerBase
{
    private readonly RepricerDbContext _dbContext;

    public PricingRulesController(RepricerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("{productId:guid}")]
    public async Task<ActionResult> GetByProductId(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var rule = await _dbContext.PricingRules
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ProductId == productId,
                cancellationToken);

        return rule is null ? NotFound() : Ok(rule);
    }

    [HttpPost]
    public async Task<ActionResult> Create(
        CreatePricingRuleRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MinimumPrice <= 0)
            return BadRequest("MinimumPrice must be greater than zero.");

        if (request.MaximumPrice < request.MinimumPrice)
            return BadRequest("MaximumPrice cannot be lower than MinimumPrice.");

        if (request.AdjustmentValue < 0)
            return BadRequest("AdjustmentValue cannot be negative.");

        var product = await _dbContext.Products
            .FirstOrDefaultAsync(
                x => x.Id == request.ProductId,
                cancellationToken);

        if (product is null)
            return NotFound("Product does not exist.");

        var ruleExists = await _dbContext.PricingRules
            .AnyAsync(
                x => x.ProductId == request.ProductId,
                cancellationToken);

        if (ruleExists)
            return Conflict("Product already has a pricing rule.");

        var rule = new PricingRule
        {
            ProductId = request.ProductId,
            Strategy = request.Strategy,
            MinimumPrice = request.MinimumPrice,
            MaximumPrice = request.MaximumPrice,
            AdjustmentValue = request.AdjustmentValue,
            MinimumProfitPercentage = request.MinimumProfitPercentage,
            IsActive = true
        };

        _dbContext.PricingRules.Add(rule);

        product.IsRepricingEnabled = true;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetByProductId),
            new { productId = product.Id },
            new
            {
                rule.Id,
                rule.ProductId,
                rule.Strategy,
                rule.MinimumPrice,
                rule.MaximumPrice,
                rule.AdjustmentValue,
                rule.MinimumProfitPercentage,
                rule.IsActive
            });
    }
}
