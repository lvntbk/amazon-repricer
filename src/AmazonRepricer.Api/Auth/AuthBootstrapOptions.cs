namespace AmazonRepricer.Api.Auth;

public sealed class AuthBootstrapOptions
{
    public const string SectionName =
        "AuthBootstrap";

    public bool Enabled { get; init; }

    public string? AdminEmail { get; init; }

    public string? AdminPassword { get; init; }
}
