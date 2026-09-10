using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AmazonRepricer.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AmazonRepricer.Api.Auth;

public sealed class AccessTokenService : IAccessTokenService
{
    private readonly JwtOptions _jwtOptions;

    public AccessTokenService(
        IOptions<JwtOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions.Value;
    }

    public string CreateAccessToken(
        AppUser user,
        IEnumerable<string> roles,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        var claims =
            new List<Claim>
            {
                new(
                    ClaimTypes.NameIdentifier,
                    user.Id.ToString()),
                new(
                    JwtRegisteredClaimNames.Jti,
                    Guid.NewGuid().ToString("N"))
            };

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(
                new Claim(
                    ClaimTypes.Email,
                    user.Email));
        }

        claims.AddRange(
            roles.Select(
                role =>
                    new Claim(
                        ClaimTypes.Role,
                        role)));

        var key =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    _jwtOptions.SigningKey));

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

        var token =
            new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                claims: claims,
                notBefore: issuedAtUtc.UtcDateTime,
                expires: expiresAtUtc.UtcDateTime,
                signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }
}
