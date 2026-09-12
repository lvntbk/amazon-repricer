using AmazonRepricer.Infrastructure.Amazon;

namespace AmazonRepricer.Tests.Amazon;

public sealed class AmazonSpApiOptionsValidationTests
{
    [Fact]
    public void Validate_RealAmazonWithoutCredentials_Fails()
    {
        var options = new AmazonSpApiOptions
        {
            UseMock = false,
            Endpoint = "https://sellingpartnerapi-eu.amazon.com",
            MarketplaceId = "A33AVAJ2PDY3EV",
            SellerId = "SELLER-001",
            ClientId = "",
            ClientSecret = "",
            RefreshToken = ""
        };

        var validator =
            new AmazonSpApiOptionsValidator();

        var result =
            validator.Validate(null, options);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Validate_ValidRealAmazonOptions_Succeeds()
    {
        var options = new AmazonSpApiOptions
        {
            UseMock = false,
            Endpoint = "https://sellingpartnerapi-eu.amazon.com",
            MarketplaceId = "A33AVAJ2PDY3EV",
            SellerId = "SELLER-001",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RefreshToken = "refresh-token"
        };

        var validator = new AmazonSpApiOptionsValidator();

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MockWithoutCredentials_Succeeds()
    {
        var options = new AmazonSpApiOptions
        {
            UseMock = true,
            Endpoint = "https://sandbox.sellingpartnerapi-eu.amazon.com"
        };

        var validator = new AmazonSpApiOptionsValidator();

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

}
