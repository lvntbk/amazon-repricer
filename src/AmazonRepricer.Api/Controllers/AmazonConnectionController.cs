using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Infrastructure.Amazon.Sellers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Api.Controllers;

[ApiController]
[Route("api/amazon")]
public sealed class AmazonConnectionController : ControllerBase
{
    private readonly IAmazonSellersClient _sellersClient;
    private readonly AmazonSpApiOptions _options;

    public AmazonConnectionController(
        IAmazonSellersClient sellersClient,
        IOptions<AmazonSpApiOptions> options)
    {
        _sellersClient = sellersClient;
        _options = options.Value;
    }

    [HttpGet("connection-test")]
    public async Task<IActionResult> TestConnection(
        CancellationToken cancellationToken)
    {
        var marketplaces =
            await _sellersClient.GetMarketplaceParticipationsAsync(
                cancellationToken);

        return Ok(new
        {
            connected = true,
            environment = _options.Endpoint.Contains(
                "sandbox",
                StringComparison.OrdinalIgnoreCase)
                    ? "Sandbox"
                    : "Production",
            configuredMarketplaceId = _options.MarketplaceId,
            configuredMarketplaceReturned = marketplaces.Any(
                x => x.Marketplace.Id == _options.MarketplaceId),
            marketplaces
        });
    }
}
