using System.Security.Claims;
using AmazonRepricer.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Api.Auth;

public static class AppJwtClaimTypes
{
    public const string SecurityStamp =
        "auth_security_stamp";
}

public interface IAccessTokenStateValidator
{
    Task<bool> IsValidAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}

public sealed class AccessTokenStateValidator
    : IAccessTokenStateValidator
{
    private readonly AuthDbContext _dbContext;

    public AccessTokenStateValidator(
        AuthDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> IsValidAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userIdValue =
            principal.FindFirstValue(
                ClaimTypes.NameIdentifier);

        var tokenSecurityStamp =
            principal.FindFirstValue(
                AppJwtClaimTypes.SecurityStamp);

        if (!Guid.TryParse(
                userIdValue,
                out var userId) ||
            string.IsNullOrWhiteSpace(
                tokenSecurityStamp))
        {
            return false;
        }

        var userState =
            await _dbContext.Users
                .AsNoTracking()
                .Where(x => x.Id == userId)
                .Select(
                    x => new
                    {
                        x.IsActive,
                        x.SecurityStamp
                    })
                .SingleOrDefaultAsync(
                    cancellationToken);

        return userState is not null &&
               userState.IsActive &&
               !string.IsNullOrWhiteSpace(
                   userState.SecurityStamp) &&
               string.Equals(
                   userState.SecurityStamp,
                   tokenSecurityStamp,
                   StringComparison.Ordinal);
    }
}
