namespace AmazonRepricer.Application.Auth;

public interface IRefreshTokenGenerator
{
    GeneratedRefreshToken Generate();

    string Hash(
        string plaintextToken);
}

public sealed record GeneratedRefreshToken(
    string PlaintextToken,
    string TokenHash);
