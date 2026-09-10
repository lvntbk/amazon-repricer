using AmazonRepricer.Infrastructure.Identity;

namespace AmazonRepricer.Api.Auth;

public interface IAccessTokenService
{
    string CreateAccessToken(
        AppUser user,
        IEnumerable<string> roles,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc);
}
