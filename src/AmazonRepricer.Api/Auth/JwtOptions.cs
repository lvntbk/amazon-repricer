namespace AmazonRepricer.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName =
        "Jwt";

    public string Issuer { get; init; } =
        string.Empty;

    public string Audience { get; init; } =
        string.Empty;

    public string SigningKey { get; init; } =
        string.Empty;

    public int AccessTokenLifetimeMinutes { get; init; } =
        15;

    public int RefreshTokenLifetimeDays { get; init; } =
        30;
}
