using Microsoft.Extensions.Options;

namespace AmazonRepricer.Infrastructure.Amazon;

public sealed class AmazonSpApiOptionsValidator
    : IValidateOptions<AmazonSpApiOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        AmazonSpApiOptions options)
    {
        var failures = new List<string>();

        if (!IsValidHttpsUrl(options.Endpoint))
        {
            failures.Add(
                "Amazon SP-API endpoint must be a valid HTTPS URL.");
        }

        if (!IsValidHttpsUrl(options.LwaEndpoint))
        {
            failures.Add(
                "Amazon LWA endpoint must be a valid HTTPS URL.");
        }

        if (!options.UseMock)
        {
            Require(
                options.MarketplaceId,
                "Amazon marketplace id is required.",
                failures);

            Require(
                options.SellerId,
                "Amazon seller id is required.",
                failures);

            Require(
                options.ClientId,
                "Amazon client id is required.",
                failures);

            Require(
                options.ClientSecret,
                "Amazon client secret is required.",
                failures);

            Require(
                options.RefreshToken,
                "Amazon refresh token is required.",
                failures);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void Require(
        string value,
        string message,
        ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(message);
        }
    }

    private static bool IsValidHttpsUrl(string value)
    {
        return Uri.TryCreate(
                   value,
                   UriKind.Absolute,
                   out var uri)
               && uri.Scheme == Uri.UriSchemeHttps;
    }
}
