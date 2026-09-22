using AmazonRepricer.Application.Amazon;

namespace AmazonRepricer.Application.Pricing;

public sealed record ListingPriceExpectation(
    string SellerId,
    string Sku,
    string MarketplaceId,
    string CurrencyCode,
    decimal ProposedPrice,
    DateTimeOffset SubmittedAtUtc);

public sealed record ListingPriceVerificationResult(
    bool IsVerified,
    decimal? VerifiedPrice,
    string Reason);

public static class ListingPriceVerificationPolicy
{
    public static ListingPriceVerificationResult Evaluate(
        ListingPriceExpectation expected,
        AmazonListingObservation observed,
        DateTimeOffset nowUtc,
        TimeSpan maximumObservationAge)
    {
        if (string.IsNullOrWhiteSpace(expected.SellerId) ||
            string.IsNullOrWhiteSpace(expected.Sku) ||
            string.IsNullOrWhiteSpace(expected.MarketplaceId) ||
            string.IsNullOrWhiteSpace(expected.CurrencyCode) ||
            expected.ProposedPrice <= 0 ||
            expected.SubmittedAtUtc == default ||
            maximumObservationAge <= TimeSpan.Zero)
        {
            return Pending("Verification inputs are invalid.");
        }

        if (!string.Equals(expected.SellerId, observed.SellerId,
                StringComparison.Ordinal) ||
            !string.Equals(expected.Sku, observed.Sku,
                StringComparison.Ordinal) ||
            !string.Equals(expected.MarketplaceId, observed.MarketplaceId,
                StringComparison.Ordinal))
        {
            return Pending("Listing identity does not match.");
        }

        if (observed.ReadStartedAtUtc < expected.SubmittedAtUtc ||
            observed.ReadCompletedAtUtc < observed.ReadStartedAtUtc ||
            observed.ReadCompletedAtUtc > nowUtc ||
            nowUtc - observed.ReadStartedAtUtc > maximumObservationAge)
        {
            return Pending("Observation timing is invalid or stale.");
        }

        if (observed.Offers is null || observed.Issues is null)
        {
            return Pending("Observation data is incomplete.");
        }

        // Conservatively block ERROR and unrecognized severity values.
        if (observed.Issues.Any(issue =>
                issue is null ||
                !(string.Equals(issue.Severity, "WARNING",
                      StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(issue.Severity, "INFO",
                      StringComparison.OrdinalIgnoreCase))))
        {
            return Pending("Listing contains errors or unrecognized issues.");
        }

        var candidates = observed.Offers
            .Where(offer =>
                offer is not null &&
                string.Equals(offer.MarketplaceId, expected.MarketplaceId,
                    StringComparison.Ordinal) &&
                string.Equals(offer.OfferType, "B2C",
                    StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length != 1)
        {
            return Pending("A unique matching B2C offer is required.");
        }

        var candidate = candidates[0];

        if (!string.Equals(candidate.CurrencyCode, expected.CurrencyCode,
                StringComparison.Ordinal))
        {
            return Pending("Offer currency does not match.");
        }

        if (candidate.Price is null ||
            candidate.Price <= 0 ||
            candidate.Price != expected.ProposedPrice)
        {
            return Pending("The proposed price is not currently observed.");
        }

        return new ListingPriceVerificationResult(
            true,
            candidate.Price,
            "The expected price was observed on the matching listing.");
    }

    private static ListingPriceVerificationResult Pending(string reason) =>
        new(false, null, reason);
}
