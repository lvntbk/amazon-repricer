using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;

namespace AmazonRepricer.Tests.Application.Pricing;

public sealed class ListingPriceVerificationPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static ListingPriceExpectation Expected() =>
        new("SELLER", "SKU", "MARKET", "TRY", 99m, Now.AddMinutes(-1));

    private static AmazonListingObservation Observation() =>
        new(
            "SELLER",
            "SKU",
            "MARKET",
            Now.AddSeconds(-10),
            Now.AddSeconds(-9),
            new[] { new AmazonListingOffer("MARKET", "B2C", 99m, "TRY") },
            Array.Empty<AmazonListingIssue>());

    [Fact]
    public void MatchingCurrentOffer_VerifiesObservedPrice()
    {
        var result = ListingPriceVerificationPolicy.Evaluate(
            Expected(), Observation(), Now, TimeSpan.FromMinutes(2));

        Assert.True(result.IsVerified);
        Assert.Equal(99m, result.VerifiedPrice);
    }

    [Fact]
    public void Warning_DoesNotPreventMatchingPriceVerification()
    {
        var observation = Observation() with
        {
            Issues = new[]
            {
                new AmazonListingIssue(
                    "WARNING_CODE", "WARNING", "Review listing.",
                    Array.Empty<string>())
            }
        };

        var result = ListingPriceVerificationPolicy.Evaluate(
            Expected(), observation, Now, TimeSpan.FromMinutes(2));

        Assert.True(result.IsVerified);
    }

    [Theory]
    [InlineData("seller")]
    [InlineData("sku")]
    [InlineData("marketplace")]
    [InlineData("offer-marketplace")]
    [InlineData("currency")]
    [InlineData("b2b")]
    [InlineData("missing-offer")]
    [InlineData("multiple-offers")]
    [InlineData("missing-price")]
    [InlineData("wrong-price")]
    [InlineData("before-submission")]
    [InlineData("future")]
    [InlineData("reversed-time")]
    [InlineData("stale")]
    [InlineData("error")]
    [InlineData("unknown-severity")]
    public void InsufficientEvidence_DoesNotVerify(string scenario)
    {
        var expected = Expected();
        var observation = Observation();
        var offer = observation.Offers[0];

        observation = scenario switch
        {
            "seller" => observation with { SellerId = "OTHER" },
            "sku" => observation with { Sku = "OTHER" },
            "marketplace" => observation with { MarketplaceId = "OTHER" },
            "offer-marketplace" => observation with
            {
                Offers = new[] { offer with { MarketplaceId = "OTHER" } }
            },
            "currency" => observation with
            {
                Offers = new[] { offer with { CurrencyCode = "USD" } }
            },
            "b2b" => observation with
            {
                Offers = new[] { offer with { OfferType = "B2B" } }
            },
            "missing-offer" => observation with
            {
                Offers = Array.Empty<AmazonListingOffer>()
            },
            "multiple-offers" => observation with
            {
                Offers = new[] { offer, offer }
            },
            "missing-price" => observation with
            {
                Offers = new[] { offer with { Price = null } }
            },
            "wrong-price" => observation with
            {
                Offers = new[] { offer with { Price = 100m } }
            },
            "before-submission" => observation with
            {
                ReadStartedAtUtc = expected.SubmittedAtUtc.AddSeconds(-1)
            },
            "future" => observation with
            {
                ReadCompletedAtUtc = Now.AddSeconds(1)
            },
            "reversed-time" => observation with
            {
                ReadCompletedAtUtc = observation.ReadStartedAtUtc.AddSeconds(-1)
            },
            "stale" => observation with
            {
                ReadStartedAtUtc = Now.AddMinutes(-3),
                ReadCompletedAtUtc = Now.AddMinutes(-3).AddSeconds(1)
            },
            "error" => observation with
            {
                Issues = new[]
                {
                    new AmazonListingIssue(
                        "PRICE_ERROR", "ERROR", "Price issue.",
                        new[] { "purchasable_offer" })
                }
            },
            "unknown-severity" => observation with
            {
                Issues = new[]
                {
                    new AmazonListingIssue(
                        "UNKNOWN", "", "Unknown issue.",
                        Array.Empty<string>())
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        if (scenario == "stale")
        {
            expected = expected with { SubmittedAtUtc = Now.AddMinutes(-5) };
        }

        var result = ListingPriceVerificationPolicy.Evaluate(
            expected, observation, Now, TimeSpan.FromMinutes(2));

        Assert.False(result.IsVerified);
        Assert.Null(result.VerifiedPrice);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }
}
