using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;

internal static class AuthTestTokenFactory
{
    public static string Create(
        string role,
        Guid? userId = null,
        string? issuer = null,
        string? audience = null,
        string? signingKey = null,
        DateTime? notBeforeUtc = null,
        DateTime? expiresAtUtc = null)
    {
        var resolvedIssuer =
            issuer ??
            AuthTestEnvironmentScope.DefaultIssuer;

        var resolvedAudience =
            audience ??
            AuthTestEnvironmentScope.DefaultAudience;

        var resolvedSigningKey =
            signingKey ??
            AuthTestEnvironmentScope.DefaultSigningKey;

        var now =
            DateTime.UtcNow;

        var key =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    resolvedSigningKey));

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

        var token =
            new JwtSecurityToken(
                issuer: resolvedIssuer,
                audience: resolvedAudience,
                claims:
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        (userId ?? Guid.NewGuid())
                            .ToString()),
                    new Claim(
                        ClaimTypes.Role,
                        role)
                ],
                notBefore:
                    notBeforeUtc ??
                    now.AddMinutes(-1),
                expires:
                    expiresAtUtc ??
                    now.AddMinutes(10),
                signingCredentials:
                    credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }
}
