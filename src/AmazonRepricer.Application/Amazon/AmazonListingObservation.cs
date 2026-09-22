namespace AmazonRepricer.Application.Amazon;

// A read of Amazon's current listing data.
// Receiving this data does not itself prove a submission was applied.
public sealed record AmazonListingObservation(
    string SellerId,
    string Sku,
    string MarketplaceId,
    DateTimeOffset ReadStartedAtUtc,
    DateTimeOffset ReadCompletedAtUtc,
    IReadOnlyList<AmazonListingOffer> Offers,
    IReadOnlyList<AmazonListingIssue> Issues);

public sealed record AmazonListingOffer(
    string MarketplaceId,
    string OfferType,
    decimal? Price,
    string? CurrencyCode);

public sealed record AmazonListingIssue(
    string Code,
    string Severity,
    string Message,
    IReadOnlyList<string> AttributeNames);
