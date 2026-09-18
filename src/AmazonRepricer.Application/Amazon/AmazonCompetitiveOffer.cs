namespace AmazonRepricer.Application.Amazon;

public sealed record AmazonCompetitiveOffer(
    string SellerId,
    string Condition,
    string FulfillmentType,
    decimal ListingPrice,
    decimal ShippingPrice,
    decimal LandedPrice,
    string CurrencyCode,
    bool IsOurs);
