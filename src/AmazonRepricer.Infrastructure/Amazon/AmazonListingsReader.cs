using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonRepricer.Application.Amazon;

namespace AmazonRepricer.Infrastructure.Amazon;

public sealed class AmazonListingsReader : IAmazonListingReader
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

    private readonly HttpClient _httpClient;
    private readonly ILwaAccessTokenProvider _accessTokenProvider;

    public AmazonListingsReader(
        HttpClient httpClient,
        ILwaAccessTokenProvider accessTokenProvider)
    {
        _httpClient = httpClient;
        _accessTokenProvider = accessTokenProvider;
    }

    public async Task<AmazonListingObservation> GetListingAsync(
        string sellerId,
        string sku,
        string marketplaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sellerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(marketplaceId);

        var accessToken = await _accessTokenProvider
            .GetAccessTokenAsync(cancellationToken);

        var path =
            $"listings/2021-08-01/items/{Uri.EscapeDataString(sellerId)}/" +
            $"{Uri.EscapeDataString(sku)}" +
            $"?marketplaceIds={Uri.EscapeDataString(marketplaceId)}" +
            "&includedData=offers,issues";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(
            "x-amz-access-token",
            accessToken);

        var startedAtUtc = DateTimeOffset.UtcNow;

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var listing = await response.Content
            .ReadFromJsonAsync<ListingReadResponse>(
                JsonOptions,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Amazon listing response was empty.");

        if (!string.Equals(listing.Sku, sku, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Amazon listing response SKU does not match the requested SKU.");
        }

        var offers = (listing.Offers ?? [])
            .Select(offer => new AmazonListingOffer(
                offer.MarketplaceId ?? string.Empty,
                offer.OfferType ?? string.Empty,
                offer.Price?.Amount,
                offer.Price?.CurrencyCode))
            .ToArray();

        var issues = (listing.Issues ?? [])
            .Select(issue => new AmazonListingIssue(
                issue.Code ?? string.Empty,
                issue.Severity ?? string.Empty,
                issue.Message ?? string.Empty,
                issue.AttributeNames ?? []))
            .ToArray();

        return new AmazonListingObservation(
            sellerId,
            listing.Sku!,
            marketplaceId,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            offers,
            issues);
    }
}

internal sealed class ListingReadResponse
{
    public string? Sku { get; set; }
    public List<ListingReadOffer>? Offers { get; set; }
    public List<ListingReadIssue>? Issues { get; set; }
}

internal sealed class ListingReadOffer
{
    public string? MarketplaceId { get; set; }
    public string? OfferType { get; set; }
    public ListingReadMoney? Price { get; set; }
}

internal sealed class ListingReadMoney
{
    public decimal? Amount { get; set; }
    public string? CurrencyCode { get; set; }
}

internal sealed class ListingReadIssue
{
    public string? Code { get; set; }
    public string? Severity { get; set; }
    public string? Message { get; set; }
    public string[]? AttributeNames { get; set; }
}
