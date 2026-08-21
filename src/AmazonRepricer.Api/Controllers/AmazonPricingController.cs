using AmazonRepricer.Infrastructure.Amazon;
using Microsoft.AspNetCore.Mvc;

namespace AmazonRepricer.Api.Controllers;

[ApiController]
[Route("api/amazon/pricing")]
public sealed class AmazonPricingController : ControllerBase
{
    private readonly AmazonSpApiPricingProvider _pricingProvider;

    public AmazonPricingController(
        AmazonSpApiPricingProvider pricingProvider)
    {
        _pricingProvider = pricingProvider;
    }

    [HttpGet]
    public async Task<IActionResult> GetPricing(
        [FromQuery] string asin,
        [FromQuery] string sku,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(asin) ||
            string.IsNullOrWhiteSpace(sku))
        {
            return BadRequest("ASIN and SKU are required.");
        }

        var result = await _pricingProvider.GetPricingAsync(
            asin,
            sku,
            cancellationToken);

        return Ok(new
        {
            asin,
            sku,
            result.FeaturedOfferPrice,
            result.IsFeaturedOfferOurs
        });
    }
}
