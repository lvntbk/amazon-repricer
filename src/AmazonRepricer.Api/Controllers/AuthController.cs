using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AmazonRepricer.Api.Auth;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AmazonRepricer.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private static readonly TimeSpan AccessTokenLifetime =
        TimeSpan.FromMinutes(15);

    private static readonly TimeSpan RefreshTokenLifetime =
        TimeSpan.FromDays(30);

    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILoginTimingProtector _loginTimingProtector;
    private readonly AuthDbContext _authDbContext;
    private readonly IRefreshTokenGenerator _refreshTokenGenerator;

    public AuthController(
        UserManager<AppUser> userManager,
        IConfiguration configuration,
        ILoginTimingProtector loginTimingProtector,
        AuthDbContext authDbContext,
        IRefreshTokenGenerator refreshTokenGenerator)
    {
        _userManager = userManager;
        _configuration = configuration;
        _loginTimingProtector = loginTimingProtector;
        _authDbContext = authDbContext;
        _refreshTokenGenerator = refreshTokenGenerator;
    }

    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Login)]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Unauthorized();
        }

        var user =
            await _userManager.FindByEmailAsync(
                request.Email.Trim());

        if (user is null)
        {
            _loginTimingProtector.VerifyDummyPassword(
                request.Password);

            return Unauthorized();
        }

        if (!user.IsActive)
        {
            _loginTimingProtector.VerifyDummyPassword(
                request.Password);

            return Unauthorized();
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            _loginTimingProtector.VerifyDummyPassword(
                request.Password);

            return Unauthorized();
        }

        var passwordIsValid =
            await _userManager.CheckPasswordAsync(
                user,
                request.Password);

        if (!passwordIsValid)
        {
            var accessFailedResult =
                await _userManager.AccessFailedAsync(user);

            if (!accessFailedResult.Succeeded)
            {
                return Unauthorized();
            }

            return Unauthorized();
        }

        var resetResult =
            await _userManager.ResetAccessFailedCountAsync(user);

        if (!resetResult.Succeeded)
        {
            return Unauthorized();
        }

        var roles =
            await _userManager.GetRolesAsync(user);

        var now =
            DateTimeOffset.UtcNow;

        var expiresAtUtc =
            now.Add(AccessTokenLifetime);

        var refreshTokenExpiresAtUtc =
            now.Add(RefreshTokenLifetime);

        var accessToken =
            CreateAccessToken(
                user,
                roles,
                now,
                expiresAtUtc);

        var generatedRefreshToken =
            _refreshTokenGenerator.Generate();

        var refreshToken =
            new AuthRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FamilyId = Guid.NewGuid(),
                TokenHash =
                    generatedRefreshToken.TokenHash,
                CreatedAtUtc =
                    now.UtcDateTime,
                ExpiresAtUtc =
                    refreshTokenExpiresAtUtc.UtcDateTime
            };

        _authDbContext.RefreshTokens.Add(
            refreshToken);

        await _authDbContext.SaveChangesAsync(
            HttpContext.RequestAborted);

        return Ok(
            new LoginResponse(
                accessToken,
                "Bearer",
                expiresAtUtc,
                generatedRefreshToken.PlaintextToken,
                refreshTokenExpiresAtUtc));
    }

    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Refresh)]
    [HttpPost("refresh")]
    public async Task<ActionResult<LoginResponse>> Refresh(
        RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(
            request.RefreshToken))
        {
            return Unauthorized();
        }

        var cancellationToken =
            HttpContext.RequestAborted;

        var tokenHash =
            _refreshTokenGenerator.Hash(
                request.RefreshToken);

        var now =
            DateTimeOffset.UtcNow;

        var existingRefreshToken =
            await _authDbContext.RefreshTokens
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.TokenHash == tokenHash,
                    cancellationToken);

        if (existingRefreshToken is null)
        {
            return Unauthorized();
        }

        if (existingRefreshToken.ReplacedByTokenId is not null)
        {
            await RevokeRefreshTokenFamilyForReplayAsync(
                existingRefreshToken.UserId,
                existingRefreshToken.FamilyId,
                now.UtcDateTime,
                cancellationToken);

            return Unauthorized();
        }

        if (existingRefreshToken.RevokedAtUtc is not null ||
            existingRefreshToken.ExpiresAtUtc <=
                now.UtcDateTime)
        {
            return Unauthorized();
        }

        var user =
            await _userManager.FindByIdAsync(
                existingRefreshToken.UserId.ToString());

        if (user is null ||
            !user.IsActive ||
            await _userManager.IsLockedOutAsync(user))
        {
            return Unauthorized();
        }

        var roles =
            await _userManager.GetRolesAsync(user);

        var generatedRefreshToken =
            _refreshTokenGenerator.Generate();

        var replacementTokenId =
            Guid.NewGuid();

        var refreshTokenExpiresAtUtc =
            now.Add(RefreshTokenLifetime);

        await using var transaction =
            await _authDbContext.Database
                .BeginTransactionAsync(
                    cancellationToken);

        _authDbContext.RefreshTokens.Add(
            new AuthRefreshToken
            {
                Id = replacementTokenId,
                UserId = user.Id,
                FamilyId =
                    existingRefreshToken.FamilyId,
                TokenHash =
                    generatedRefreshToken.TokenHash,
                CreatedAtUtc =
                    now.UtcDateTime,
                ExpiresAtUtc =
                    refreshTokenExpiresAtUtc.UtcDateTime
            });

        await _authDbContext.SaveChangesAsync(
            cancellationToken);

        var rotatedCount =
            await _authDbContext.RefreshTokens
                .Where(
                    x =>
                        x.Id ==
                            existingRefreshToken.Id &&
                        x.RevokedAtUtc == null &&
                        x.ReplacedByTokenId == null &&
                        x.ExpiresAtUtc >
                            now.UtcDateTime)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                x => x.RevokedAtUtc,
                                now.UtcDateTime)
                            .SetProperty(
                                x => x.ReplacedByTokenId,
                                replacementTokenId)
                            .SetProperty(
                                x => x.RevocationReason,
                                "Rotated"),
                    cancellationToken);

        if (rotatedCount != 1)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            await transaction.DisposeAsync();

            await RevokeRefreshTokenFamilyForReplayAsync(
                existingRefreshToken.UserId,
                existingRefreshToken.FamilyId,
                now.UtcDateTime,
                cancellationToken);

            return Unauthorized();
        }

        await transaction.CommitAsync(
            cancellationToken);

        var accessTokenExpiresAtUtc =
            now.Add(AccessTokenLifetime);

        var accessToken =
            CreateAccessToken(
                user,
                roles,
                now,
                accessTokenExpiresAtUtc);

        return Ok(
            new LoginResponse(
                accessToken,
                "Bearer",
                accessTokenExpiresAtUtc,
                generatedRefreshToken.PlaintextToken,
                refreshTokenExpiresAtUtc));
    }

    private async Task RevokeRefreshTokenFamilyForReplayAsync(
        Guid userId,
        Guid familyId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken)
    {
        await _authDbContext.RefreshTokens
            .Where(
                x =>
                    x.UserId == userId &&
                    x.FamilyId == familyId &&
                    x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(
                            x => x.RevokedAtUtc,
                            x =>
                                x.CreatedAtUtc > revokedAtUtc
                                    ? x.CreatedAtUtc
                                    : revokedAtUtc)
                        .SetProperty(
                            x => x.RevocationReason,
                            "ReplayDetected"),
                cancellationToken);
    }

    private string CreateAccessToken(
        AppUser user,
        IEnumerable<string> roles,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        var issuer =
            _configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException(
                "JWT issuer is required.");

        var audience =
            _configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException(
                "JWT audience is required.");

        var signingKey =
            _configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException(
                "JWT signing key is required.");

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
                Encoding.UTF8.GetBytes(signingKey));

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

        var token =
            new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: issuedAtUtc.UtcDateTime,
                expires: expiresAtUtc.UtcDateTime,
                signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }

    public sealed record LoginRequest(
        string Email,
        string Password);

    public sealed record RefreshRequest(
        string RefreshToken);

    public sealed record LoginResponse(
        string AccessToken,
        string TokenType,
        DateTimeOffset ExpiresAtUtc,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAtUtc);
}
