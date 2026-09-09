using AmazonRepricer.Infrastructure.Identity;

namespace AmazonRepricer.Tests.Auth;

public sealed class RefreshTokenGeneratorTests
{
    [Fact]
    public void Generate_Creates256BitOpaqueTokenAndMatchingSha256Hash()
    {
        var generator =
            new RefreshTokenGenerator();

        var generated =
            generator.Generate();

        Assert.Equal(
            64,
            generated.PlaintextToken.Length);

        Assert.True(
            generated.PlaintextToken.All(
                Uri.IsHexDigit));

        Assert.Equal(
            64,
            generated.TokenHash.Length);

        Assert.True(
            generated.TokenHash.All(
                Uri.IsHexDigit));

        Assert.Equal(
            generated.TokenHash,
            generator.Hash(
                generated.PlaintextToken));
    }

    [Fact]
    public void Generate_Twice_CreatesDifferentTokens()
    {
        var generator =
            new RefreshTokenGenerator();

        var first =
            generator.Generate();

        var second =
            generator.Generate();

        Assert.NotEqual(
            first.PlaintextToken,
            second.PlaintextToken);

        Assert.NotEqual(
            first.TokenHash,
            second.TokenHash);
    }
}
