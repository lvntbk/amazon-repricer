using AmazonRepricer.Api.Contracts.Stores;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Api.Controllers;

[ApiController]
[Route("api/amazon-stores")]
public sealed class AmazonStoresController : ControllerBase
{
    private readonly RepricerDbContext _dbContext;

    public AmazonStoresController(RepricerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AmazonStore>>> GetAll(
        CancellationToken cancellationToken)
    {
        var stores = await _dbContext.AmazonStores
            .AsNoTracking()
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(stores);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AmazonStore>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var store = await _dbContext.AmazonStores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return store is null
            ? NotFound()
            : Ok(store);
    }

    [HttpPost]
    public async Task<ActionResult<AmazonStore>> Create(
        CreateAmazonStoreRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.SellerId) ||
            string.IsNullOrWhiteSpace(request.MarketplaceId))
        {
            return BadRequest("Name, SellerId and MarketplaceId are required.");
        }

        var exists = await _dbContext.AmazonStores
            .AnyAsync(
                x => x.SellerId == request.SellerId &&
                     x.MarketplaceId == request.MarketplaceId,
                cancellationToken);

        if (exists)
        {
            return Conflict(
                "This seller is already registered for the marketplace.");
        }

        var store = new AmazonStore
        {
            Name = request.Name.Trim(),
            SellerId = request.SellerId.Trim(),
            MarketplaceId = request.MarketplaceId.Trim()
        };

        _dbContext.AmazonStores.Add(store);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = store.Id },
            store);
    }
}
