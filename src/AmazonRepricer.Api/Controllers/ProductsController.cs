using AmazonRepricer.Api.Contracts.Products;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly RepricerDbContext _dbContext;

    public ProductsController(RepricerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(
        CancellationToken cancellationToken)
    {
        var products = await _dbContext.Products
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.AmazonStoreId,
                x.Sku,
                x.Asin,
                x.Title,
                x.Cost,
                x.CurrentPrice,
                x.IsRepricingEnabled,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(products);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.AmazonStoreId,
                x.Sku,
                x.Asin,
                x.Title,
                x.Cost,
                x.CurrentPrice,
                x.IsRepricingEnabled,
                x.CreatedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        return product is null
            ? NotFound()
            : Ok(product);
    }

    [HttpPost]
    public async Task<ActionResult> Create(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sku) ||
            string.IsNullOrWhiteSpace(request.Asin) ||
            string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("Sku, Asin and Title are required.");
        }

        if (request.Cost is <= 0)
            return BadRequest("Cost must be greater than zero.");

        if (request.CurrentPrice is <= 0)
            return BadRequest("CurrentPrice must be greater than zero.");

        var storeExists = await _dbContext.AmazonStores
            .AnyAsync(x => x.Id == request.AmazonStoreId, cancellationToken);

        if (!storeExists)
            return BadRequest("Amazon store does not exist.");

        var duplicateSku = await _dbContext.Products
            .AnyAsync(
                x => x.AmazonStoreId == request.AmazonStoreId &&
                     x.Sku == request.Sku,
                cancellationToken);

        if (duplicateSku)
            return Conflict("SKU already exists in this store.");

        var product = new Product
        {
            AmazonStoreId = request.AmazonStoreId,
            Sku = request.Sku.Trim(),
            Asin = request.Asin.Trim(),
            Title = request.Title.Trim(),
            Cost = request.Cost,
            CurrentPrice = request.CurrentPrice,
            IsRepricingEnabled = false
        };

        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = product.Id },
            new
            {
                product.Id,
                product.AmazonStoreId,
                product.Sku,
                product.Asin,
                product.Title,
                product.Cost,
                product.CurrentPrice,
                product.IsRepricingEnabled
            });
    }
}
